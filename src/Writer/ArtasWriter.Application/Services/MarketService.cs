using ArtasCore.Abstractions;
using ArtasCore.Models;

namespace ArtasWriter.Application.Services;

/// <summary>
/// Сервис рыночных данных: формирует <see cref="MarketPairBufferItem"/> из событий стакана Finam.
/// Аналог Python <c>MarketService</c> (середина стакана + открытый интерес).
/// Возвращает только две центральные пары (лучший Ask и лучший Bid).
/// </summary>
public sealed class MarketService
{
    private readonly int _digits;

    public MarketService(int digits)
    {
        _digits = digits;
    }

    /// <summary>Текущее состояние рынка.</summary>
    public MarketPairBufferItem CurrentMarkets { get; private set; } = CreateEmpty();

    /// <summary>Предыдущее состояние рынка.</summary>
    public MarketPairBufferItem PreviousMarkets { get; private set; } = CreateEmpty();

    /// <summary>Последний стакан: [лучший Ask, лучший Bid].</summary>
    public IReadOnlyList<(double Price, int Volume)> CurrentBook { get; private set; } =
        Array.Empty<(double, int)>();

    /// <summary>Открытый интерес (обновляется по событию Finam).</summary>
    public long Interest { get; private set; }

    /// <summary>
    /// Получено ли хотя бы одно значение открытого интереса от Finam.
    /// До первого события <see cref="Interest"/> равен 0, но это не «реальный»
    /// ноль рынка, а отсутствие данных. Флаг позволяет не публиковать фракталы
    /// с фиктивным нулём (аналог прогрева, см. Python tick_service.py).
    /// </summary>
    public bool InterestInitialized { get; private set; }

    /// <summary>Создать пустое состояние рынка.</summary>
    public static MarketPairBufferItem CreateEmpty() =>
        new()
        {
            MarketPairs = new[]
            {
                new MarketPair(0.0, 0),
                new MarketPair(0.0, 0),
            },
            Interest = 0,
        };

    /// <summary>
    /// Обновить открытый интерес.
    /// Ноль — валидное значение (начало сессии / отсутствие накопленных позиций),
    /// поэтому принимается как есть, без подмены предыдущим значением.
    /// Порядок событий гарантируется на уровне подписки
    /// (см. <c>FinamProducer.StartAsync</c>: интерес подписывается раньше котировок).
    /// </summary>
    public void UpdateInterest(long interest)
    {
        Interest = interest;
        InterestInitialized = true;
    }

    /// <summary>
    /// Обновить стакан из события Finam. Берём две центральные пары: лучший Ask
    /// (минимальная цена продажи) и лучший Bid (максимальная цена покупки).
    /// </summary>
    /// <param name="evt">Событие стакана Finam.</param>
    /// <param name="referencePrice">
    /// Опорная цена (последняя цена сделки). Используется для отсечения «мусорных»
    /// уровней: Ask должен быть выше опорной цены, Bid — ниже. Если 0 — фильтр не применяется.
    /// </param>
    public void UpdateOrderBook(OrderBookEvent evt, double referencePrice = 0.0)
    {
        var ask = BestAsk(evt.Asks, referencePrice);
        var bid = BestBid(evt.Bids, referencePrice);

        if (ask is null || bid is null)
        {
            return;
        }

        var sellPair = new MarketPair(Round(ask.Value.Price), ask.Value.Volume);
        var buyPair = new MarketPair(Round(bid.Value.Price), bid.Value.Volume);

        PreviousMarkets = CurrentMarkets;
        CurrentMarkets = new MarketPairBufferItem
        {
            MarketPairs = new[] { sellPair, buyPair },
            Interest = Interest,
        };

        CurrentBook = new[]
        {
            (sellPair.Price, sellPair.Volume),
            (buyPair.Price, buyPair.Volume),
        };
    }

    /// <summary>
    /// Лучший Ask. <paramref name="asks"/> уже отсортированы по возрастанию цены
    /// (см. <see cref="OrderBookEvent"/>), поэтому берём первый валидный уровень
    /// с ценой не ниже опорной (это и есть минимальная цена продажи).
    /// </summary>
    private static (double Price, int Volume)? BestAsk(
        IReadOnlyList<(double Price, int Volume)> asks, double referencePrice)
    {
        foreach (var level in asks)
        {
            if (level.Volume <= 0)
            {
                continue;
            }

            if (referencePrice > 0 && level.Price < referencePrice)
            {
                continue;
            }

            return level;
        }

        return null;
    }

    /// <summary>
    /// Лучший Bid. <paramref name="bids"/> уже отсортированы по убыванию цены
    /// (см. <see cref="OrderBookEvent"/>), поэтому берём первый валидный уровень
    /// с ценой не выше опорной (это и есть максимальная цена покупки).
    /// </summary>
    private static (double Price, int Volume)? BestBid(
        IReadOnlyList<(double Price, int Volume)> bids, double referencePrice)
    {
        foreach (var level in bids)
        {
            if (level.Volume <= 0)
            {
                continue;
            }

            if (referencePrice > 0 && level.Price > referencePrice)
            {
                continue;
            }

            return level;
        }

        return null;
    }

    private double Round(double value) => Math.Round(value, _digits, MidpointRounding.ToEven);
}
