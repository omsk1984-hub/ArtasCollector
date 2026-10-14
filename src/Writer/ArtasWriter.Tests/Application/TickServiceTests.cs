using ArtasWriter.Application.Services;
using ArtasCore.Abstractions;
using ArtasCore.Models;
using Xunit;

namespace ArtasWriter.Tests.Application;

/// <summary>
/// Тесты <see cref="TickService"/>: конвертация события Finam в MyTick,
/// обновление стакана/интереса, детекция фрактала.
/// </summary>
public class TickServiceTests
{
    private static SymbolInfo Symbol() => new()
    {
        Id = 1,
        Name = "SiZ6",
        Symbol = "Si",
        Digits = 3,
        Point = 0.001,
    };

    [Fact]
    public void ConvertToMyTick_RoundsPrices()
    {
        var service = new TickService(Symbol());
        var evt = new QuoteEvent("SiZ6", Bid: 1.0004, Ask: 1.0006, Last: 1.0005,
            Volume: 5, Flags: (int)TickFlag.Buy, TimeMsc: 1000);

        var tick = service.ConvertToMyTick(evt);

        // Округление банковское (ToEven): 1.0004→1.000, 1.0006→1.001, 1.0005→1.000.
        Assert.Equal(1.000, tick.Bid, precision: 3);
        Assert.Equal(1.001, tick.Ask, precision: 3);
        Assert.Equal(1.000, tick.Last, precision: 3);
        Assert.Equal(5, tick.Volume);
        Assert.True(tick.IsBuy);
    }

    [Fact]
    public void OnOrderBook_UpdatesMarketState()
    {
        var service = new TickService(Symbol());
        var bids = new List<(double, int)> { (1.000, 10), (0.999, 20) };
        var asks = new List<(double, int)> { (1.001, 15), (1.002, 25) };

        service.OnOrderBook(new OrderBookEvent("SiZ6", bids, asks, 1000));

        Assert.Equal(1.001, service.Market.CurrentMarkets.MAsk.Price, precision: 3);
        Assert.Equal(1.000, service.Market.CurrentMarkets.MBid.Price, precision: 3);
    }

    [Fact]
    public void ConvertToMyTick_UsesOrderBook_WhenAvailable()
    {
        var service = new TickService(Symbol());
        var bids = new List<(double, int)> { (1.000, 10), (0.999, 20) };
        var asks = new List<(double, int)> { (1.001, 15), (1.002, 25) };
        service.OnOrderBook(new OrderBookEvent("SiZ6", bids, asks, 1000));

        // Котировочные Ask/Bid нулевые (как в реальном Finam-событии),
        // но стакан уже получен — market_pairs должны прийти из стакана с объёмами.
        var tick = service.ConvertToMyTick(
            new QuoteEvent("SiZ6", Bid: 0, Ask: 0, Last: 1.0005,
                Volume: 5, Flags: (int)TickFlag.Buy, TimeMsc: 1001));

        Assert.Equal(1.001, tick.MAsk.Price, precision: 3);
        Assert.Equal(1.000, tick.MBid.Price, precision: 3);
        Assert.True(tick.MAsk.Volume > 0);
        Assert.True(tick.MBid.Volume > 0);
    }

    [Fact]
    public void OnOrderBook_IgnoresStaleLevels_FarFromLastPrice()
    {
        var service = new TickService(Symbol());

        // Опора — последняя цена сделки 1.000 (как в реальном Finam Last).
        service.Init(new MyTick { Last = 1.000, Flags = 0, TimeMsc = 0 });

        // «Мусорные» уровни Finam: SellSize 0.850 (объём 1) и BuySize 0.840.
        var bids = new List<(double, int)> { (1.000, 10), (0.999, 20), (0.840, 1) };
        var asks = new List<(double, int)> { (1.001, 15), (1.002, 25), (0.850, 1) };

        service.OnOrderBook(new OrderBookEvent("SiZ6", bids, asks, 1000));

        // Лучший Ask — минимальный выше Last (1.001), а не мусорный 0.850.
        Assert.Equal(1.001, service.Market.CurrentMarkets.MAsk.Price, precision: 3);
        // Лучший Bid — максимальный ниже Last (1.000), мусорный 0.840 игнорируется.
        Assert.Equal(1.000, service.Market.CurrentMarkets.MBid.Price, precision: 3);
    }

    [Fact]
    public void ConvertToMyTick_FallsBackToQuote_WhenNoOrderBook()
    {
        var service = new TickService(Symbol());

        var tick = service.ConvertToMyTick(
            new QuoteEvent("SiZ6", Bid: 1.000, Ask: 1.001, Last: 1.0005,
                Volume: 5, Flags: (int)TickFlag.Buy, TimeMsc: 1000));

        // Стакан ещё не приходил — fallback на цены котировки (объём 0).
        Assert.Equal(1.001, tick.MAsk.Price, precision: 3);
        Assert.Equal(1.000, tick.MBid.Price, precision: 3);
        Assert.Equal(0, tick.MAsk.Volume);
        Assert.Equal(0, tick.MBid.Volume);
    }

    [Fact]
    public void OnOpenInterest_UpdatesInterest()
    {
        var service = new TickService(Symbol());

        service.OnOpenInterest(new OpenInterestEvent("SiZ6", 4242, 1000));

        Assert.Equal(4242, service.Market.Interest);

        // Интерес попадает в конвертированный тик.
        var tick = service.ConvertToMyTick(
            new QuoteEvent("SiZ6", 1.0, 1.0, 1.0, 1, (int)TickFlag.Buy, 1000));
        Assert.Equal(4242, tick.Interest);
    }

    [Fact]
    public void OnOpenInterest_Zero_IsAcceptedAsValid()
    {
        var service = new TickService(Symbol());

        service.OnOpenInterest(new OpenInterestEvent("SiZ6", 4242, 1000));
        Assert.Equal(4242, service.Market.Interest);

        // Ноль — валидное значение (начало сессии / сброс позиций),
        // он не должен подменяться предыдущим ненулевым интересом.
        service.OnOpenInterest(new OpenInterestEvent("SiZ6", 0, 1001));
        Assert.Equal(0, service.Market.Interest);

        var tick = service.ConvertToMyTick(
            new QuoteEvent("SiZ6", 1.0, 1.0, 1.0, 1, (int)TickFlag.Buy, 1002));
        Assert.Equal(0, tick.Interest);
    }

    [Fact]
    public void OnQuote_FirstQuote_WarmsUpFactory_NoFractal()
    {
        var service = new TickService(Symbol());

        // Первый «направленный» тик — прогрев состояния фабрики (аналог Python init),
        // фрактал на нём не формируется даже при большом движении.
        var result = service.OnQuote(
            new QuoteEvent("SiZ6", 1.0, 1.0, 86031.0, 1, (int)TickFlag.Sell, 1000));

        Assert.Null(result);
        Assert.True(service.Market.Interest == 0);
    }

    [Fact]
    public void OnQuote_NeutralFlags_DoesNotCreateFractal()
    {
        var service = new TickService(Symbol());
        service.Init(new MyTick { Last = 1.0, Flags = 0, TimeMsc = 0 });

        var result = service.OnQuote(
            new QuoteEvent("SiZ6", 1.0, 1.0, 1.0, 1, Flags: 0, TimeMsc: 1000));

        Assert.Null(result);
    }

    [Fact]
    public void OnQuote_IncreasingBuy_CreatesDownFractal()
    {
        var service = new TickService(Symbol());
        // Интерес получен — фракталы публикуются (иначе идёт прогрев, см. гейт ниже).
        service.OnOpenInterest(new OpenInterestEvent("SiZ6", 1000, 900));
        // Первый Sell-тик задаёт нижний пик (Min=1.000), как в Python calculate_fractal.
        service.OnQuote(new QuoteEvent("SiZ6", 1.0, 1.0, 1.000, 1, (int)TickFlag.Sell, 1000));

        Fractal? fractal = null;
        for (var i = 1; i <= 60 && fractal is null; i++)
        {
            fractal = service.OnQuote(new QuoteEvent(
                "SiZ6", 1.0, 1.0, 1.000 + i * 0.001, 1, (int)TickFlag.Buy, 1000 + i));
        }

        Assert.NotNull(fractal);
        Assert.False(fractal!.IsUp);
        Assert.Equal(1.000, fractal.Price, precision: 3);
    }

    [Fact]
    public void OnQuote_WithoutInterest_DoesNotCreateFractal()
    {
        var service = new TickService(Symbol());

        // Первый Sell-тик задаёт нижний пик (Min), как в Python calculate_fractal.
        var fractal = service.OnQuote(
            new QuoteEvent("SiZ6", 1.0, 1.0, 1.000, 1, (int)TickFlag.Sell, 1000));
        // Движение вверх сформировало бы фрактал, но интерес ещё не приходил.
        for (var i = 1; i <= 60 && fractal is null; i++)
        {
            fractal = service.OnQuote(new QuoteEvent(
                "SiZ6", 1.0, 1.0, 1.000 + i * 0.001, 1, (int)TickFlag.Buy, 1000 + i));
        }

        Assert.Null(fractal);
        Assert.False(service.Market.InterestInitialized);
    }

    [Fact]
    public void OnQuote_AfterInterest_CreatesFractalWithActualInterest()
    {
        var service = new TickService(Symbol());

        // Прогрев без интереса — фабрика не трогается (гейт).
        service.OnQuote(new QuoteEvent("SiZ6", 1.0, 1.0, 1.000, 1, (int)TickFlag.Sell, 1000));
        for (var i = 1; i <= 30; i++)
        {
            service.OnQuote(new QuoteEvent(
                "SiZ6", 1.0, 1.0, 1.000 + i * 0.001, 1, (int)TickFlag.Buy, 1000 + i));
        }

        // Приходит интерес: дальше фабрика прогревается уже с ним.
        service.OnOpenInterest(new OpenInterestEvent("SiZ6", 5555, 2000));

        // Sell задаёт нижний пик (TickPeak с реальным интересом), Buy двигает цену вверх.
        service.OnQuote(new QuoteEvent("SiZ6", 1.0, 1.0, 1.000, 1, (int)TickFlag.Sell, 2100));

        Fractal? fractal = null;
        for (var i = 31; i <= 120 && fractal is null; i++)
        {
            fractal = service.OnQuote(new QuoteEvent(
                "SiZ6", 1.0, 1.0, 1.000 + i * 0.001, 1, (int)TickFlag.Buy, 1000 + i));
        }

        Assert.NotNull(fractal);
        Assert.True(service.Market.InterestInitialized);
        Assert.Equal(5555, fractal!.Interest);
    }
}
