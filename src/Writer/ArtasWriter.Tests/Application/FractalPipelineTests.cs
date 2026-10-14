using System.Threading.Channels;
using ArtasWriter.Application.Pipeline;
using ArtasWriter.Application.Producers;
using ArtasWriter.Application.Services;
using ArtasCore.Abstractions;
using ArtasCore.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ArtasWriter.Tests.Application;

/// <summary>
/// Тесты <see cref="FractalPipeline"/>: маршрутизация событий и запись фракталов.
/// </summary>
public class FractalPipelineTests
{
    private static SymbolInfo Symbol() => new()
    {
        Id = 7,
        Name = "SiZ6",
        Symbol = "Si",
        Digits = 3,
        Point = 0.001,
    };

    private static (FractalPipeline pipeline, Channel<FractalRecord> channel) Create()
    {
        var channel = Channel.CreateUnbounded<FractalRecord>();
        var tickService = new TickService(Symbol());
        var pipeline = new FractalPipeline(
            tickService, 7, channel.Writer, NullLogger.Instance);
        return (pipeline, channel);
    }

    [Fact]
    public void Process_OrderBookAndInterest_NoFractal()
    {
        var (pipeline, channel) = Create();

        pipeline.Process(new FinamEvent(new OrderBookEvent(
            "SiZ6", new List<(double, int)> { (1.0, 1) }, new List<(double, int)> { (1.1, 1) }, 1000)));
        pipeline.Process(new FinamEvent(new OpenInterestEvent("SiZ6", 500, 1000)));

        Assert.False(channel.Reader.TryRead(out _));
        Assert.Equal(0, pipeline.ProcessedTicks);
    }

    [Fact]
    public void Process_QuoteIncrementsCounter()
    {
        var (pipeline, _) = Create();

        pipeline.Process(new FinamEvent(new QuoteEvent(
            "SiZ6", 1.0, 1.0, 1.0, 1, (int)TickFlag.Buy, 1000)));

        Assert.Equal(1, pipeline.ProcessedTicks);
    }

    [Fact]
    public void Process_SustainedMovement_WritesFractalToChannel()
    {
        var (pipeline, channel) = Create();

        // Открытый интерес приходит ДО котировок (как в исправленном Finam-потоке):
        // без него фракталы не публикуются (фиктивный ноль не «замораживается»).
        pipeline.Process(new FinamEvent(new OpenInterestEvent("SiZ6", 1234, 900)));

        // Инициализация нижнего пика.
        pipeline.Process(new FinamEvent(new QuoteEvent(
            "SiZ6", 1.0, 1.0, 1.000, 1, (int)TickFlag.Sell, 1000)));

        for (var i = 1; i <= 40 && channel.Reader.Count == 0; i++)
        {
            pipeline.Process(new FinamEvent(new QuoteEvent(
                "SiZ6", 1.0, 1.0, 1.000 + i * 0.001, 1, (int)TickFlag.Buy, 1000 + i)));
        }

        Assert.True(channel.Reader.Count > 0);
        Assert.True(channel.Reader.TryRead(out var record));
        Assert.Equal(7, record.SymbolId);
        Assert.False(record.Fractal.IsUp);

        // Фрактал несёт актуальный открытый интерес.
        Assert.Equal(1234, record.Fractal.Interest);
    }

    [Fact]
    public void Process_QuoteWithoutInterest_DoesNotWriteFractal()
    {
        var (pipeline, channel) = Create();

        // Первый Sell-тик задаёт нижний пик (Min), как в Python calculate_fractal.
        pipeline.Process(new FinamEvent(new QuoteEvent(
            "SiZ6", 1.0, 1.0, 1.000, 1, (int)TickFlag.Sell, 1000)));

        // Интерес ещё не приходил — фракталы публиковаться не должны,
        // иначе в БД уйдёт фиктивный interest = 0.
        for (var i = 1; i <= 40; i++)
        {
            pipeline.Process(new FinamEvent(new QuoteEvent(
                "SiZ6", 1.0, 1.0, 1.000 + i * 0.001, 1, (int)TickFlag.Buy, 1000 + i)));
        }

        Assert.Equal(0, channel.Reader.Count);

        // После прихода интереса фрактал формируется и содержит ненулевой интерес.
        pipeline.Process(new FinamEvent(new OpenInterestEvent("SiZ6", 4321, 2000)));

        // Sell задаёт нижний пик (TickPeak уже с реальным интересом).
        pipeline.Process(new FinamEvent(new QuoteEvent(
            "SiZ6", 1.0, 1.0, 1.000, 1, (int)TickFlag.Sell, 2100)));

        FractalRecord? captured = null;
        for (var i = 41; i <= 120 && captured is null; i++)
        {
            pipeline.Process(new FinamEvent(new QuoteEvent(
                "SiZ6", 1.0, 1.0, 1.000 + i * 0.001, 1, (int)TickFlag.Buy, 1000 + i)));
            if (channel.Reader.TryRead(out var rec))
            {
                captured = rec;
            }
        }

        Assert.NotNull(captured);
        Assert.Equal(4321, captured!.Value.Fractal.Interest);
    }

    [Fact]
    public void Process_WithOrderBook_FractalCarriesNonZeroMarketPairs()
    {
        var (pipeline, channel) = Create();

        // Стакан и интерес приходят ДО котировок (как в реальном Finam).
        var bids = new List<(double, int)> { (1.000, 10), (0.999, 20), (0.998, 30) };
        var asks = new List<(double, int)> { (1.001, 15), (1.002, 25), (1.003, 35) };
        pipeline.Process(new FinamEvent(new OrderBookEvent("SiZ6", bids, asks, 900)));
        pipeline.Process(new FinamEvent(new OpenInterestEvent("SiZ6", 777, 950)));

        // Котировки (сделки) с нулевыми Ask/Bid — типично для Finam SubscribeLatestTrades.
        pipeline.Process(new FinamEvent(new QuoteEvent(
            "SiZ6", 0, 0, 1.000, 1, (int)TickFlag.Sell, 1000)));

        FractalRecord? captured = null;
        for (var i = 1; i <= 60 && captured is null; i++)
        {
            pipeline.Process(new FinamEvent(new QuoteEvent(
                "SiZ6", 0, 0, 1.000 + i * 0.001, 1, (int)TickFlag.Buy, 1000 + i)));
            if (channel.Reader.TryRead(out var rec))
            {
                captured = rec;
            }
        }

        Assert.NotNull(captured);
        var peak = captured!.Value.Fractal.TickPeak;
        var ask = peak.MarketPairs[0];
        var bid = peak.MarketPairs[1];

        // market_pairs должны прийти из стакана (цены и объёмы не нулевые).
        Assert.True(ask.Price > 0, $"Ask.price={ask.Price}");
        Assert.True(bid.Price > 0, $"Bid.price={bid.Price}");
        Assert.True(ask.Volume > 0, $"Ask.volume={ask.Volume}");
        Assert.True(bid.Volume > 0, $"Bid.volume={bid.Volume}");
    }
}
