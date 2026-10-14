using ArtasCore.Abstractions;
using ArtasWriter.Application.Producers;
using Microsoft.Extensions.Logging;

namespace ArtasWriter.Worker;

/// <summary>
/// Разделяемое состояние сервиса: активные конвейеры инструментов и статусы соединений.
/// Регистрируется как singleton; доступ к полям синхронизирован. Реализует
/// <see cref="IWriterState"/> для инверсии зависимостей потребителей.
/// </summary>
public sealed class WriterState : IWriterState
{
    private readonly List<InstrumentPipeline> _pipelines = new();

    // Статусы стримов по символу Finam (ticker@mic): kind -> StreamStatus.
    private readonly Dictionary<string, Dictionary<StreamKind, StreamStatus>> _streams =
        new(StringComparer.Ordinal);

    private readonly object _sync = new();
    private readonly ILogger<WriterState> _logger;

    private bool _dbConnected;

    public WriterState(ILogger<WriterState> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public DateTime StartTimeUtc { get; } = DateTime.UtcNow;

    /// <inheritdoc />
    public bool FinamConnected
    {
        get
        {
            lock (_sync)
            {
                return ComputeFinamConnected();
            }
        }
    }

    /// <inheritdoc />
    public bool DbConnected
    {
        get
        {
            lock (_sync)
            {
                return _dbConnected;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<InstrumentPipeline> Pipelines
    {
        get
        {
            lock (_sync)
            {
                return _pipelines.ToList();
            }
        }
    }

    /// <inheritdoc />
    public void SetDbConnected(bool connected)
    {
        bool changed;
        lock (_sync)
        {
            changed = _dbConnected != connected;
            _dbConnected = connected;
        }

        // Логируем только переходы (важно для алертов: Потеря/восстановление PG).
        if (changed)
        {
            if (connected)
            {
                _logger.LogInformation("PostgreSQL доступен — соединение восстановлено");
            }
            else
            {
                _logger.LogWarning("PostgreSQL недоступен — соединение потеряно");
            }
        }
    }

    /// <inheritdoc />
    public void SetStreamStatus(string finamSymbol, StreamKind kind, bool up, string? reason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(finamSymbol);

        bool kindChanged;
        bool symbolConnectedBefore;
        bool symbolConnectedAfter;

        lock (_sync)
        {
            // Агрегированное состояние финама ДО изменения (false, если хоть один
            // известный стрим упал) — чтобы поймать сквозной переход health.
            symbolConnectedBefore = ComputeFinamConnected();

            if (!_streams.TryGetValue(finamSymbol, out var byKind))
            {
                byKind = new Dictionary<StreamKind, StreamStatus>();
                _streams[finamSymbol] = byKind;
            }

            var previous = byKind.TryGetValue(kind, out var prev) ? prev : (StreamStatus?)null;
            byKind[kind] = new StreamStatus(up, up ? null : reason);

            kindChanged = previous is null || previous.Value.Up != up;
            symbolConnectedAfter = ComputeFinamConnected();
        }

        if (kindChanged)
        {
            if (up)
            {
                _logger.LogInformation(
                    "Finam стрим {Kind} для {Symbol} восстановлен", kind, finamSymbol);
            }
            else
            {
                _logger.LogWarning(
                    "Finam стрим {Kind} для {Symbol} остановлен: {Reason}",
                    kind, finamSymbol, reason ?? "неизвестная причина");
            }
        }

        // Сквозной переход finam_connected (для алертов в Loki).
        if (symbolConnectedBefore != symbolConnectedAfter)
        {
            if (symbolConnectedAfter)
            {
                _logger.LogInformation("Finam: соединение восстановлено (все стримы активны)");
            }
            else
            {
                _logger.LogWarning("Finam: соединение потеряно (есть остановленные стримы)");
            }
        }
    }

    /// <summary>Агрегированное состояние Finam: false при любом упавшем стриме. Вызывать под <c>_sync</c>.</summary>
    private bool ComputeFinamConnected()
    {
        foreach (var byKind in _streams.Values)
        {
            foreach (var status in byKind.Values)
            {
                if (!status.Up)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<StreamKind, StreamStatus> GetStreamStatuses(string finamSymbol)
    {
        if (string.IsNullOrWhiteSpace(finamSymbol))
        {
            return new Dictionary<StreamKind, StreamStatus>();
        }

        lock (_sync)
        {
            if (_streams.TryGetValue(finamSymbol, out var byKind))
            {
                return new Dictionary<StreamKind, StreamStatus>(byKind);
            }

            return new Dictionary<StreamKind, StreamStatus>();
        }
    }

    /// <inheritdoc />
    public void SetPipelines(IEnumerable<InstrumentPipeline> pipelines)
    {
        ArgumentNullException.ThrowIfNull(pipelines);
        List<InstrumentPipeline> snapshot;
        lock (_sync)
        {
            _pipelines.Clear();
            _pipelines.AddRange(pipelines);
            snapshot = _pipelines.ToList();
        }

        foreach (var p in snapshot)
        {
            _logger.LogInformation(
                "Активен конвейер {Symbol} → {FinamSymbol}", p.ProjectSymbol, p.FinamSymbol);
        }
    }
}
