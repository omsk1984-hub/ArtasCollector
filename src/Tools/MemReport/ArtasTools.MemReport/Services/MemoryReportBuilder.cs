using System.Globalization;
using ArtasTools.MemReport.Analysis;
using ArtasTools.MemReport.Models;
using ArtasTools.MemReport.Options;

namespace ArtasTools.MemReport.Services;

/// <summary>
/// Строит отчёт по утечке памяти: определяет доступные метрики, считает
/// тренды и формирует итоговую оценку вероятности утечки.
///
/// Ключевые принципы, снижающие ложные срабатывания:
/// — тренд оценивается только по сериям с достаточным числом точек и длиной окна;
/// — вердикт строится по наихудшему (максимальному) наклону среди ДОСТОВЕРНЫХ
///   серий одного инстанса, а не как сумма наклонов всех (в т.ч. мёртвых) серий;
/// — учитывается тренд нижней огибающей (post-GC минимумы) — устойчивый рост
///   базовой линии heap является признаком долгоживущих (Gen2) объектов.
/// </summary>
public sealed class MemoryReportBuilder
{
    private const double Mb = 1024.0 * 1024.0;

    /// <summary>Описание метрики-кандидата.</summary>
    private sealed record MetricSpec(
        string Key,
        string Description,
        string Unit,
        bool IsBytes,
        Func<string, string> BuildQuery);

    private readonly PrometheusClient _client;

    public MemoryReportBuilder(PrometheusClient client) => _client = client;

    private static string LabelFilter(string job) =>
        string.IsNullOrWhiteSpace(job) ? "" : $"{{service_name=\"{job}\"}}";

    // Кандидаты метрик памяти (.NET runtime instrumentation → OTel → Prometheus).
    // Имена после prometheusremotewrite: точки → подчёркивания, +суффиксы типа.
    // ВАЖНО: имена соответствуют фактической runtime-инструментации .NET 10
    // (System.Runtime Meter), проверены запросом /api/v1/label/__name__/values.
    private static IReadOnlyList<MetricSpec> BuildSpecs(string job)
    {
        var f = LabelFilter(job);
        return new[]
        {
            new MetricSpec("managed_heap", "Managed heap (GC heap size, последняя сборка)", "MB", true,
                _ => $"dotnet_gc_last_collection_heap_size_bytes{f}"),
            new MetricSpec("rss", "Process working set (RSS)", "MB", true,
                _ => $"dotnet_process_memory_working_set_bytes{f}"),
            new MetricSpec("gc_committed", "Committed heap (последняя сборка)", "MB", true,
                _ => $"dotnet_gc_last_collection_memory_committed_size_bytes{f}"),
            new MetricSpec("gc_fragmentation", "Heap fragmentation (последняя сборка)", "MB", true,
                _ => $"dotnet_gc_last_collection_heap_fragmentation_size_bytes{f}"),
            // Размер heap по поколениям — позволяет отделить долгоживущие (Gen2/LOH)
            // объекты от кратковременных. Рост Gen2/LOH — сильный признак утечки.
            new MetricSpec("heap_gen", "Heap size по поколениям (Gen0/1/2/LOH)", "MB", true,
                _ => $"dotnet_gc_heap_size_bytes{f}"),
            new MetricSpec("alloc_rate", "Allocation rate", "MB/s", true,
                s => $"rate(dotnet_gc_heap_allocated_bytes_total{f}[{s}])"),
            new MetricSpec("gc_collections", "GC collections by generation", "1/s", false,
                s => $"sum(rate(dotnet_gc_collections_total{f}[{s}])) by (generation)"),
            new MetricSpec("gc_pause", "GC pause time rate", "s/s", false,
                s => $"rate(dotnet_gc_pause_time_seconds_total{f}[{s}])"),
            new MetricSpec("threads", "Thread pool thread count", "шт", false,
                _ => $"dotnet_thread_pool_thread_count_total{f}")
        };
    }

    /// <summary>Сформировать отчёт за указанное окно.</summary>
    public async Task<MemoryReport> BuildAsync(ReportOptions options, CancellationToken ct = default)
    {
        var specs = BuildSpecs(options.Job);
        var findings = new List<MetricFinding>();

        foreach (var spec in specs)
        {
            var query = spec.BuildQuery(options.Step);
            var resp = await _client.QueryRangeAsync(query, options.Hours, options.Step, ct);
            if (resp?.Data?.Result is null || resp.Data.Result.Count == 0)
                continue;

            foreach (var series in resp.Data.Result)
            {
                var points = new List<(long T, double V)>(series.Values.Count);
                foreach (var pair in series.Values)
                {
                    if (pair.Count < 2) continue;
                    points.Add((pair[0].AsLong(), pair[1].AsDouble()));
                }
                if (points.Count == 0) continue;

                var trend = TrendAnalyzer.Analyze(points);

                // Байтовые метрики нормализуем в МБ, числовые (count/rate) — как есть.
                double Norm(double v) => spec.IsBytes ? v / Mb : v;

                var slopeDisplay = Norm(trend.SlopePerHour);
                var baselineDisplay = Norm(trend.BaselineSlopePerHour);
                var reliable = trend.IsReliable(options.MinSamples, options.MinDurationMinutes * 60L);

                findings.Add(new MetricFinding
                {
                    Metric = spec.Key,
                    Description = spec.Description,
                    Unit = spec.Unit,
                    Generation = series.Metric.TryGetValue("generation", out var g) ? g : "",
                    Instance = series.Metric.TryGetValue("service_instance_id", out var i) ? i : "",
                    Samples = trend.Samples,
                    First = Norm(trend.First),
                    Last = Norm(trend.Last),
                    Min = Norm(trend.Min),
                    Max = Norm(trend.Max),
                    SlopePerHour = slopeDisplay,
                    R2 = trend.R2,
                    IsBytes = spec.IsBytes,
                    FirstTimestamp = trend.FirstTimestamp,
                    LastTimestamp = trend.LastTimestamp,
                    DurationSeconds = trend.DurationSeconds,
                    BaselineSlopePerHour = baselineDisplay,
                    BaselineR2 = trend.BaselineR2,
                    BaselineSamples = trend.BaselineSamples,
                    Reliable = reliable
                });
            }
        }

        var verdict = ComputeVerdict(findings, options);

        return new MemoryReport
        {
            GeneratedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            PrometheusUrl = options.PrometheusUrl,
            WindowHours = options.Hours,
            Step = options.Step,
            Job = options.Job,
            Verdict = verdict,
            Findings = findings
        };
    }

    /// <summary>
    /// Оценка вероятности утечки по ключевым метрикам (heap / Gen2 / RSS).
    /// Вердикт строится по максимальному наклону среди ДОСТОВЕРНЫХ серий
    /// (достаточно точек и длинное окно), а не как сумма по всем сериям:
    /// суммирование по мёртвым инстансам и коротким стартовым сериям даёт
    /// ложноположительные вердикты.
    /// </summary>
    private static LeakVerdict ComputeVerdict(List<MetricFinding> findings, ReportOptions options)
    {
        var reliable = findings.Where(f => f.Reliable).ToList();

        // Ключевые «сигнальные» метрики: managed heap (в т.ч. Gen2/LOH) и RSS.
        var heap = reliable.Where(f => f.Metric == "managed_heap").ToList();
        var gen2 = reliable
            .Where(f => f.Metric == "heap_gen" &&
                        (f.Generation is "generation:2" or "gen2" or "2" or "generation:loh" or "loh"))
            .ToList();
        var rss = reliable.Where(f => f.Metric == "rss").ToList();

        // Наихудший (максимальный) наклон среди достоверных серий каждой группы.
        double heapSlope = MaxSlope(heap);
        double gen2Slope = MaxSlope(gen2);
        double rssSlope = MaxSlope(rss);
        double heapR2 = heap.Count > 0 ? heap.Max(f => Safe(f.R2)) : 0.0;

        // Consider также baseline (post-GC) тренд heap — сигнал устойчивого роста.
        double heapBaselineSlope = heap.Count > 0 ? heap.Max(f => Safe(f.BaselineSlopePerHour)) : 0.0;

        var level = "OK";
        var reason = "Признаков устойчивого роста памяти не обнаружено.";

        if (reliable.Count == 0)
        {
            // Нет ни одной достоверной серии — судить об утечке нельзя.
            level = "OK";
            reason = "Недостаточно данных для оценки: нет серий с достаточной длительностью/числом точек " +
                     $"(требуется ≥ {options.MinSamples} точек и ≥ {options.MinDurationMinutes} мин).";
            return new LeakVerdict
            {
                Level = level,
                Reason = reason,
                HeapSlopeMbPerHour = 0,
                RssSlopeMbPerHour = 0,
                HeapR2 = 0
            };
        }

        // Порог устойчивого роста heap > 10 МБ/ч (RSS > 20 МБ/ч).
        // Gen2/LOH рост > 5 МБ/ч — отдельный усиливающий сигнал.
        if (heapSlope > 50 || rssSlope > 100 || gen2Slope > 40)
        {
            level = "CRITICAL";
            reason = "Высокая скорость роста памяти.";
        }
        else if (heapSlope > 10 || rssSlope > 20 || gen2Slope > 5)
        {
            level = "WARNING";
            reason = "Заметный устойчивый рост памяти — вероятная утечка.";
        }
        else if (heapBaselineSlope > 5)
        {
            level = "WARNING";
            reason = $"Растёт нижняя огибающая heap (post-GC baseline {heapBaselineSlope:N2} МБ/ч) — " +
                     "после сборок heap не возвращается к прежней базовой линии, вероятны долгоживущие объекты.";
        }

        if (heapR2 < 0.5 && level != "OK")
        {
            reason += $" Линейный фит слабый (R²={heapR2:F2}) — требуется больше данных.";
        }

        return new LeakVerdict
        {
            Level = level,
            Reason = reason,
            HeapSlopeMbPerHour = heapSlope,
            RssSlopeMbPerHour = rssSlope,
            HeapR2 = heapR2
        };
    }

    private static double MaxSlope(List<MetricFinding> findings) =>
        findings.Count == 0 ? 0.0 : findings.Max(f => Safe(f.SlopePerHour));

    /// <summary>Приводит значение к МБ для байтовых метрик; для остальных — без изменений.</summary>
    private static double Normalize(bool isBytes, double value) => isBytes ? value / Mb : value;

    /// <summary>Заменяет NaN/Infinity на 0 для устойчивости расчётов вердикта.</summary>
    private static double Safe(double value) => double.IsFinite(value) ? value : 0.0;
}
