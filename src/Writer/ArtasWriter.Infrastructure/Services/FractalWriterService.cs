using System.Threading.Channels;
using ArtasWriter.Application.Options;
using ArtasWriter.Application.Pipeline;
using ArtasWriter.Core;
using ArtasCore.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArtasWriter.Infrastructure.Services;

/// <summary>
/// Единственный writer-консьюмер: читает общий канал фракталов, батчит и пишет в PG.
/// Делегирует накопление батчей <see cref="WriteBatcher"/>, учёт за день —
/// <see cref="IDailyCounter"/>, фактическую запись — <see cref="IFractalRepository"/>.
/// </summary>
public sealed class FractalWriterService : IFractalWriter
{
    // Интервал сводки по записи (вместо лога на каждый батч). Значение
    // вынесено в константу: сводка — Information, детали батча — Debug.
    private static readonly TimeSpan SummaryInterval = TimeSpan.FromSeconds(60);

    private readonly IFractalRepository _repository;
    private readonly WriteBatcher _batcher;
    private readonly IDailyCounter _dailyCounter;
    private readonly ILogger<FractalWriterService> _logger;

    private long _totalWritten;

    // Накопители для периодической сводки (сбрасываются при выводе).
    private long _summaryBatches;
    private long _summaryRecords;
    private long _summaryMaxFlushMs;
    private DateTime _nextSummaryUtc = DateTime.UtcNow + SummaryInterval;

    public FractalWriterService(
        IFractalRepository repository,
        IOptions<WriterOptions> options,
        ILogger<FractalWriterService> logger,
        WriteBatcher? batcher = null,
        IDailyCounter? dailyCounter = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        var opts = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _batcher = batcher ?? new WriteBatcher(
            opts.WriteBatchSize, TimeSpan.FromMilliseconds(opts.WriteFlushIntervalMs));
        _dailyCounter = dailyCounter ?? new DailyCounter();
    }

    /// <inheritdoc />
    public long TotalWritten => Interlocked.Read(ref _totalWritten);

    /// <inheritdoc />
    public int WrittenToday => _dailyCounter.WrittenToday;

    /// <inheritdoc />
    public Task RunAsync(ChannelReader<FractalRecord> reader, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return _batcher.RunAsync(reader, FlushAsync, cancellationToken);
    }

    /// <summary>Записать накопленный батч (используется и в тестах).</summary>
    public async Task<int> FlushAsync(List<FractalRecord> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return 0;
        }

        var records = batch
            .Select(r => (r.SymbolId, r.Fractal))
            .ToList();

        // Диагностический ID батча для трекинга в логах.
        var batchId = Guid.NewGuid().ToString().Substring(0, 8);
        var startTime = DateTime.UtcNow;

        // Контекст батча на весь вызов: любые логи внутри несут batch_id
        // структурно (пригодится при переходе на структурный вывод, P2).
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["batch_id"] = batchId,
        });

        // OpenTelemetry: начинаем трейс для этого батча (no-op если OTel не подключён).
        var span = Observability.StartSpan("writer.flush_batch")
            .SetTag("batch_id", batchId)
            .SetTag("batch_size", batch.Count);

        try
        {
            var inserted = await _repository
                .SaveBatchAsync(records, cancellationToken);

            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;

            // OpenTelemetry: метрики.
            Observability.Counter("writer.fractals.written").Add(inserted);
            Observability.Histogram("writer.batch.flush_time_ms").Record(elapsed);
            Observability.Histogram("writer.batch.size").Record(batch.Count);

            Interlocked.Add(ref _totalWritten, inserted);
            _dailyCounter.Add(inserted);

            // Детали каждого батча — только Debug (иначе шум в Loki).
            // Имена полей — snake_case: их извлекает Promtail-regex
            // (batch_id=/count=/elapsed=) в structured metadata.
            _logger.LogDebug(
                "Батч batch_id={BatchId}: {Inserted}/{Total} записей за elapsed={Elapsed}ms",
                batchId, inserted, batch.Count, elapsed);

            // Агрегированная сводка раз в SummaryInterval (Information).
            AccumulateSummary(inserted, elapsed);
            return inserted;
        }
        catch (Exception ex)
        {
            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;

            // OpenTelemetry: отмечаем ошибку и инкрементим счётчик.
            span.SetStatus(SpanStatus.Error).RecordException(ex);
            Observability.Counter("writer.batch.errors").Add(1);
            Observability.Histogram("writer.batch.flush_time_ms").Record(elapsed);

            // Первые 10 идентификаторов фракталов — чтобы по ошибке понять,
            // какие именно записи не прошли (без раздувания строки).
            var ids = string.Join(",",
                batch.Take(10).Select(r => r.Fractal.Id.ToString()));
            if (batch.Count > 10)
            {
                ids += ",...";
            }

            _logger.LogError(ex,
                "Батч batch_id={BatchId}: ошибка записи count={Count} фракталов после elapsed={Elapsed}ms ids=[{Ids}]",
                batchId, batch.Count, elapsed, ids);
            return 0;
        }
        finally
        {
            span.End();
        }
    }

    /// <summary>
    /// Накопить статистику батча и, если прошёл <see cref="SummaryInterval"/>,
    /// вывести агрегированную сводку (Information) и сбросить накопители.
    /// Заменяет лог на каждый батч (был основным источником шума в Loki).
    /// </summary>
    private void AccumulateSummary(int inserted, double elapsedMs)
    {
        _summaryBatches++;
        _summaryRecords += inserted;
        if (elapsedMs > _summaryMaxFlushMs)
        {
            _summaryMaxFlushMs = (long)elapsedMs;
        }

        var now = DateTime.UtcNow;
        if (now < _nextSummaryUtc)
        {
            return;
        }

        _logger.LogInformation(
            "Запись PG за {Interval}s: батчей={Batches}, фракталов={Records}, " +
            "макс. время батча={MaxMs}ms, всего={Total}, за сегодня={Today}",
            (int)SummaryInterval.TotalSeconds,
            Interlocked.Exchange(ref _summaryBatches, 0),
            Interlocked.Exchange(ref _summaryRecords, 0),
            Interlocked.Exchange(ref _summaryMaxFlushMs, 0),
            TotalWritten,
            WrittenToday);

        _nextSummaryUtc = now + SummaryInterval;
    }
}
