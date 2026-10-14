namespace ArtasCore.Abstractions;

/// <summary>
/// Событие от Finam: текущий тик (quote).
/// </summary>
/// <param name="Symbol">Код инструмента в терминах Finam (например, "SiZ6").</param>
/// <param name="Bid">Цена Bid.</param>
/// <param name="Ask">Цена Ask.</param>
/// <param name="Last">Последняя цена сделки.</param>
/// <param name="Volume">Объем.</param>
/// <param name="Flags">Флаги тика (TICK_FLAG_*).</param>
/// <param name="TimeMsc">Время в миллисекундах (UTC).</param>
public readonly record struct QuoteEvent(
    string Symbol,
    double Bid,
    double Ask,
    double Last,
    int Volume,
    int Flags,
    long TimeMsc);

/// <summary>
/// Событие от Finam: стакан (order book).
/// </summary>
/// <param name="Symbol">Код инструмента в терминах Finam.</param>
/// <param name="Bids">Bid-уровни (цена, объем), по убыванию цены.</param>
/// <param name="Asks">Ask-уровни (цена, объем), по возрастанию цены.</param>
/// <param name="TimeMsc">Время события в миллисекундах (UTC).</param>
public readonly record struct OrderBookEvent(
    string Symbol,
    IReadOnlyList<(double Price, int Volume)> Bids,
    IReadOnlyList<(double Price, int Volume)> Asks,
    long TimeMsc);

/// <summary>
/// Событие от Finam: открытый интерес.
/// </summary>
/// <param name="Symbol">Код инструмента в терминах Finam.</param>
/// <param name="Interest">Значение открытого интереса.</param>
/// <param name="TimeMsc">Время события в миллисекундах (UTC).</param>
public readonly record struct OpenInterestEvent(
    string Symbol,
    long Interest,
    long TimeMsc);

/// <summary>
/// Вид потока данных Finam (для диагностики и health-статуса).
/// </summary>
public enum StreamKind
{
    /// <summary>Поток котировок (bid/ask/last/open_interest).</summary>
    Quotes,

    /// <summary>Поток стакана цен.</summary>
    OrderBook,

    /// <summary>Поток сделок (с направлением BUY/SELL).</summary>
    Trades,
}

/// <summary>
/// Событие отказа стрима: исчерпан лимит попыток переподключения.
/// Стрим остановлен, остальные продолжают работать.
/// </summary>
/// <param name="Symbol">Код инструмента в терминах Finam (например, "SiZ6").</param>
/// <param name="Kind">Вид потока, который остановлен.</param>
/// <param name="Reason">Причина последней ошибки (краткое описание).</param>
public readonly record struct StreamFailedEvent(
    string Symbol,
    StreamKind Kind,
    string Reason);

/// <summary>
/// Транспорт-независимый клиент Finam (WebSocket или gRPC).
/// Конкретная реализация выбирается по результатам разведки API.
/// </summary>
public interface IFinamClient : IAsyncDisposable
{
    /// <summary>Установить соединение и авторизоваться.</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Подписаться на котировки по инструменту.</summary>
    Task SubscribeQuotesAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>Подписаться на стакан по инструменту.</summary>
    Task SubscribeOrderBookAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>Подписаться на открытый интерес по инструменту.</summary>
    Task SubscribeOpenInterestAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>Событие нового котировочного тика.</summary>
    event Action<QuoteEvent>? QuoteReceived;

    /// <summary>Событие обновления стакана.</summary>
    event Action<OrderBookEvent>? OrderBookReceived;

    /// <summary>Событие обновления открытого интереса.</summary>
    event Action<OpenInterestEvent>? OpenInterestReceived;

    /// <summary>
    /// Событие отказа стрима: исчерпан лимит попыток переподключения.
    /// Подписчики (продьюсер/состояние) помечают соответствующий стрим как упавший.
    /// </summary>
    event Action<StreamFailedEvent>? StreamFailed;
}
