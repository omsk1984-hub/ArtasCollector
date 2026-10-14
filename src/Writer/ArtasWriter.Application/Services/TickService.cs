using ArtasCore.Abstractions;
using ArtasCore.Models;

namespace ArtasWriter.Application.Services;

/// <summary>
/// Сервис обработки тиков в реальном времени.
/// Аналог Python <c>TickService</c>, но источник — события Finam (без MT5 и без истории).
/// Событийная модель: обработка запускается входящими событиями, без цикла опроса.
/// </summary>
public sealed class TickService : FractalBufferService
{
    private readonly FractalFactory _factory;
    private readonly MarketService _marketService;
    private readonly int _digits;

    /// <summary>Текущий обработанный тик.</summary>
    public MyTick TickCurrent { get; private set; }

    /// <summary>Последний сформированный фрактал (null, если не сформирован).</summary>
    public Fractal? FractalCurrent2 { get; private set; }

    public TickService(SymbolInfo symbolInfo, int bufferSize = 500)
        : base(bufferSize)
    {
        ArgumentNullException.ThrowIfNull(symbolInfo);

        _digits = symbolInfo.Digits;
        _factory = new FractalFactory(symbolInfo.Digits, symbolInfo.Point);
        _marketService = new MarketService(symbolInfo.Digits);
        TickCurrent = new MyTick
        {
            TimeMsc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
    }

    /// <summary>Сервис рыночных данных.</summary>
    public MarketService Market => _marketService;

    /// <summary>Обновить стакан по событию Finam.</summary>
    public void OnOrderBook(OrderBookEvent evt)
    {
        // Опорная цена — последняя цена сделки из текущего тика. Нужна для
        // отсечения «мусорных» уровней стакана Finam (Ask выше, Bid ниже цены сделки).
        _marketService.UpdateOrderBook(evt, TickCurrent.Last);
    }

    /// <summary>Обновить открытый интерес по событию Finam.</summary>
    public void OnOpenInterest(OpenInterestEvent evt)
    {
        _marketService.UpdateInterest(evt.Interest);
    }

    /// <summary>
    /// Инициализировать модель первым тиком. Аналог Python <c>init()</c>.
    /// </summary>
    public void Init(MyTick tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        TickCurrent = tick;
        _factory.Init(tick);
        Reset();
        FractalCurrent2 = null;
    }

    /// <summary>
    /// Обработать входящий котировочный тик Finam.
    /// Аналог Python <c>calculate_fractal()</c> + <c>convert_to_my_tick()</c>.
    /// </summary>
    /// <returns>Сформированный фрактал или null.</returns>
    public Fractal? OnQuote(QuoteEvent evt)
    {
        var tick = ConvertToMyTick(evt);

        if (tick.IsBuy is null)
        {
            _factory.UpdateAb(tick);
            return null;
        }

        TickCurrent = tick;

        // Открытый интерес ещё не получен от Finam: не трогаем состояние
        // фабрики вовсе. Иначе первый пик (TickPeak) будет установлен тиком с
        // фиктивным Interest = 0 и «заморозится» в фрактале (Fractal.Interest
        // берётся из TickPeak). Как только интерес придёт, фабрика прогреется
        // первым направленным тиком ниже — уже с реальным интересом.
        // Реальный ноль начала сессии обрабатывается после InterestInitialized.
        if (!_marketService.InterestInitialized)
        {
            return null;
        }

        // Прогрев состояния фабрики первым «направленным» тиком (аналог Python
        // init(ticks[-1]) + первый calculate_fractal). Без этого Max/PrevPick равны
        // нулю и первый фрактал получил бы «мусорную» дистанцию (например,
        // отрицательную). Calculate учитывает первый тик в Min/Max, как это делает
        // Python-поток через get_fractal → calculate.
        if (!_factory.IsInitialized)
        {
            _factory.Init(tick);
            _factory.Calculate(tick);
            return null;
        }

        var fractal = _factory.GetFractal(tick);
        if (fractal is not null)
        {
            AddFractal(fractal);
            FractalCurrent2 = fractal;
            return fractal;
        }
        return null;
    }

    /// <summary>
    /// Преобразовать котировочное событие Finam в <see cref="MyTick"/>.
    /// Аналог Python <c>convert_to_my_tick(tick, buffer_item)</c>: стакан
    /// (<see cref="MyTick.MarketPairs"/>) берётся из сервиса рынка
    /// (<see cref="_marketService"/>.CurrentMarkets), а не из котировки —
    /// это обеспечивает объёмы и корректные цены стакана.
    /// </summary>
    public MyTick ConvertToMyTick(QuoteEvent evt)
    {
        return new MyTick
        {
            Volume = evt.Volume,
            Ask = Round(evt.Ask),
            Bid = Round(evt.Bid),
            Last = Round(evt.Last),
            Flags = evt.Flags,
            TimeMsc = evt.TimeMsc,
            Interest = _marketService.Interest,
            MarketPairs = ResolveMarketPairs(evt),
        };
    }

    /// <summary>
    /// Пары стакана для тика. Приоритет — актуальный стакан из
    /// <see cref="MarketService.CurrentMarkets"/> (с объёмами). Если стакан ещё
    /// не получен (пустой), используется fallback на цены котировки.
    /// Аналог Python <c>market_item.market_pairs</c> в <c>convert_to_my_tick</c>.
    /// </summary>
    private IReadOnlyList<MarketPair> ResolveMarketPairs(QuoteEvent evt)
    {
        var current = _marketService.CurrentMarkets.MarketPairs;
        if (current.Count >= 2 && current[0].Price > 0 && current[1].Price > 0)
        {
            return current;
        }

        // Fallback: стакан ещё не приходил — берём цены из котировки (объём 0).
        return new[]
        {
            new MarketPair(Round(evt.Ask), 0),
            new MarketPair(Round(evt.Bid), 0),
        };
    }

    private double Round(double value) => Math.Round(value, _digits, MidpointRounding.ToEven);
}
