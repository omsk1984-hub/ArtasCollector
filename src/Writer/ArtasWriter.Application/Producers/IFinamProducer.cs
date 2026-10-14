using ArtasCore.Abstractions;

namespace ArtasWriter.Application.Producers;

/// <summary>
/// Унифицированное событие Finam, маршрутизируемое в канал инструмента.
/// </summary>
public readonly record struct FinamEvent
{
    public FinamEvent(QuoteEvent quote) { Quote = quote; OrderBook = null; OpenInterest = null; }
    public FinamEvent(OrderBookEvent orderBook) { OrderBook = orderBook; Quote = null; OpenInterest = null; }
    public FinamEvent(OpenInterestEvent interest) { OpenInterest = interest; Quote = null; OrderBook = null; }

    /// <summary>Символ в терминах Finam.</summary>
    public string Symbol =>
        Quote?.Symbol ?? OrderBook?.Symbol ?? OpenInterest?.Symbol ?? string.Empty;

    public QuoteEvent? Quote { get; }
    public OrderBookEvent? OrderBook { get; }
    public OpenInterestEvent? OpenInterest { get; }
}

/// <summary>
/// Продьюсер: подписка на стрим Finam и публикация событий в свой канал.
/// </summary>
public interface IFinamProducer
{
    /// <summary>Символ в терминах проекта (например, "Si").</summary>
    string ProjectSymbol { get; }

    /// <summary>Символ в терминах Finam (например, "SiZ6").</summary>
    string FinamSymbol { get; }

    /// <summary>Канал событий инструмента (bounded, backpressure).</summary>
    System.Threading.Channels.ChannelReader<FinamEvent> Reader { get; }

    /// <summary>Запустить подписку и публикацию.</summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>Остановить продьюсера.</summary>
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Событие отказа стрима инструмента (исчерпан лимит ретраев).
    /// Оркестратор использует его для фиксации состояния в health.
    /// </summary>
    event Action<StreamFailedEvent>? StreamFailed;
}
