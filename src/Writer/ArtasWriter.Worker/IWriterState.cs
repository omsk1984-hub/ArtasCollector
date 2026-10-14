using ArtasCore.Abstractions;
using ArtasWriter.Application.Producers;

namespace ArtasWriter.Worker;

/// <summary>
/// Статус одного стрима Finam для символа (внутренняя модель состояния).
/// </summary>
/// <param name="Up">Стрим активен.</param>
/// <param name="Reason">Причина остановки (пусто, если активен).</param>
public readonly record struct StreamStatus(bool Up, string? Reason);

/// <summary>
/// Разделяемое состояние сервиса для health-провайдера и hosted service.
/// Абстракция вводится для инверсии зависимостей: потребители не зависят
/// от конкретного источника состояния.
/// </summary>
public interface IWriterState
{
    /// <summary>Время запуска сервиса (UTC).</summary>
    DateTime StartTimeUtc { get; }

    /// <summary>
    /// Агрегированное состояние соединения с Finam: true, если ни один стрим
    /// не упал. false — если хотя бы один стрим символа исчерпал ретраи.
    /// </summary>
    bool FinamConnected { get; }

    /// <summary>Состояние соединения с БД.</summary>
    bool DbConnected { get; }

    /// <summary>Снимок активных конвейеров.</summary>
    IReadOnlyList<InstrumentPipeline> Pipelines { get; }

    /// <summary>Зафиксировать состояние соединения с БД.</summary>
    void SetDbConnected(bool connected);

    /// <summary>
    /// Зафиксировать статус стрима Finam для символа (в терминах Finam, например "SiZ6").
    /// </summary>
    /// <param name="finamSymbol">Полный код инструмента Finam (ticker@mic).</param>
    /// <param name="kind">Вид потока (quotes/orderbook/trades).</param>
    /// <param name="up">Стрим активен (true) или остановлен по ретраям (false).</param>
    /// <param name="reason">Причина остановки (для up=false).</param>
    void SetStreamStatus(string finamSymbol, StreamKind kind, bool up, string? reason = null);

    /// <summary>
    /// Снимок статусов стримов символа (в терминах Finam). Пусто, если статусов нет.
    /// </summary>
    IReadOnlyDictionary<StreamKind, StreamStatus> GetStreamStatuses(string finamSymbol);

    /// <summary>Зафиксировать активные конвейеры.</summary>
    void SetPipelines(IEnumerable<InstrumentPipeline> pipelines);
}
