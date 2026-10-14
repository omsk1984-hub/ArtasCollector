using ArtasCore.Abstractions;
using ArtasWriter.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace ArtasWriter.Worker.Health;

/// <summary>
/// Строит <see cref="HealthSnapshot"/> из <see cref="IWriterState"/> и
/// <see cref="IFractalWriter"/>. Чистая сборка данных без побочных эффектов.
/// </summary>
public sealed class HealthSnapshotBuilder : IHealthSnapshotBuilder
{
    private readonly IWriterState _state;
    private readonly IFractalWriter _writer;
    private readonly Func<DateTime> _clock;
    private readonly ILogger<HealthSnapshotBuilder> _logger;

    public HealthSnapshotBuilder(
        IWriterState state,
        IFractalWriter writer,
        ILogger<HealthSnapshotBuilder> logger,
        Func<DateTime>? clock = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <inheritdoc />
    public HealthSnapshot Build()
    {
        var now = _clock();
        var snapshot = new HealthSnapshot
        {
            ServerTime = now,
            StartTime = _state.StartTimeUtc,
            UptimeSeconds = (long)(now - _state.StartTimeUtc).TotalSeconds,
            FinamConnected = _state.FinamConnected,
            DbConnected = _state.DbConnected,
            TotalWritten = _writer.TotalWritten,
        };

        foreach (var pipeline in _state.Pipelines)
        {
            try
            {
                var tick = pipeline.Pipeline.TickCurrent;

                // Статусы стримов символа (в терминах Finam) из разделяемого состояния.
                // В health-ответе список стримов не выводится (по требованию),
                // но per-symbol finam_connected по-прежнему вычисляется по ним.
                var statuses = _state.GetStreamStatuses(pipeline.FinamSymbol);

                // Символ «подключён», если нет ни одного упавшего стрима.
                var symbolConnected = statuses.Values.All(s => s.Up);

                snapshot.Symbols.Add(new SymbolHealth
                {
                    FinamConnected = symbolConnected,
                    Symbol = pipeline.ProjectSymbol,
                    Name = pipeline.FinamSymbol,
                    ProcessedTicks = pipeline.Pipeline.ProcessedTicks,
                    WrittenFractals = pipeline.Pipeline.WrittenFractals,
                    OpenInterest = pipeline.Pipeline.Interest,
                    // Записанных писателем фракталов за текущий UTC-день.
                    FractalsCountToday = _writer.WrittenToday,
                    LastTick = new LastTickHealth
                    {
                        Last = tick.Last,
                        Volume = tick.Volume,
                        TimeMsc = tick.TimeMsc,
                        TimeMsk = tick.TimeMsk,
                    },
                    Book = BuildBook(pipeline.Pipeline.CurrentBook),
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось собрать статус символа {Symbol}", pipeline.ProjectSymbol);
            }
        }

        // Общий статус: degraded, если БД недоступна или есть упавший стрим Finam.
        snapshot.Status = (_state.DbConnected && _state.FinamConnected) ? "ok" : "degraded";

        return snapshot;
    }

    /// <summary>
    /// Собрать стакан цен: лучший Ask и лучший Bid (две центральные пары).
    /// </summary>
    private static BookHealth? BuildBook(
        IReadOnlyList<(double Price, int Volume)> levels)
    {
        if (levels.Count == 0)
        {
            return null;
        }

        BookLevelHealth? ask = null;
        BookLevelHealth? bid = null;

        if (levels.Count > 0)
        {
            ask = new BookLevelHealth { Price = levels[0].Price, Volume = levels[0].Volume };
        }

        if (levels.Count > 1)
        {
            bid = new BookLevelHealth { Price = levels[1].Price, Volume = levels[1].Volume };
        }

        if (ask is null && bid is null)
        {
            return null;
        }

        return new BookHealth { Ask = ask, Bid = bid };
    }
}
