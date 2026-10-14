using System.Globalization;
using System.Text.Json;
using ArtasTools.ReportAnalyzer.Models;
using ArtasTools.ReportAnalyzer.Options;

namespace ArtasTools.ReportAnalyzer.Analysis;

/// <summary>
/// Анализирует каталог с JSON-отчётами MemReport: читает их, оценивает тренды
/// памяти (heap/RSS/фрагментация/аллокации), формирует список проблем и
/// итоговый вердикт. Утилита не опрашивает Prometheus — работает только с уже
/// сформированными отчётами, что позволяет анализировать историю в офлайне.
/// </summary>
public sealed class AnalysisEngine
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AnalyzerOptions _options;

    public AnalysisEngine(AnalyzerOptions options) => _options = options;

    /// <summary>Проанализировать все подходящие отчёты из каталога.</summary>
    public async Task<AnalysisResult> AnalyzeAsync(string reportsDir, CancellationToken ct = default)
    {
        var result = new AnalysisResult
        {
            GeneratedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ReportsDir = reportsDir
        };

        if (!Directory.Exists(reportsDir))
        {
            result.Verdict = "WARNING";
            result.Reason = $"Каталог отчётов не найден: {reportsDir}";
            return result;
        }

        var cutoff = _options.LastHours > 0
            ? DateTime.UtcNow.AddHours(-_options.LastHours)
            : (DateTime?)null;

        var files = Directory.EnumerateFiles(reportsDir, "memory_leak_report_*.json")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var summaries = new List<ReportSummary>();
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            var report = await TryLoadAsync(file, ct);
            if (report is null) continue;

            // Отсекаем отчёты старше окна LastHours (по имени/полю generatedAt).
            if (cutoff is not null && TryParseTimestamp(report.GeneratedAt) is { } ts && ts < cutoff)
                continue;

            summaries.Add(AnalyzeReport(file, report));
        }

        result.Reports = summaries;
        result.ReportsAnalyzed = summaries.Count;
        result.TopIssues = summaries
            .SelectMany(s => s.Issues)
            .Where(i => i.SeverityEnum != IssueSeverity.Info || i.Kind != nameof(IssueKind.DataQuality))
            .OrderByDescending(i => i.SeverityEnum)
            .ThenByDescending(i => Math.Abs(i.SlopePerHour))
            .Take(20)
            .ToList();

        ComputeVerdict(result);
        return result;
    }

    private static async Task<MemoryReport?> TryLoadAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<MemoryReport>(stream, JsonOpts, ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WARN] Не удалось прочитать {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Анализ одного отчёта → сводка с набором проблем.</summary>
    public ReportSummary AnalyzeReport(string file, MemoryReport report)
    {
        var issues = new List<AnalysisIssue>();

        // Каждая проблема помечается источником (файл + дата отчёта), чтобы в
        // сводке было видно, к какому моменту времени она относится: сигнал из
        // старого отчёта про уже несуществующий инстанс нельзя принимать за
        // актуальное состояние сервиса.
        var sourceFile = Path.GetFileName(file);
        var sourceGeneratedAt = report.GeneratedAt;

        // 1. Проброс вердикта самого отчёта. Вердикт пробрасываем только если в
        // отчёте есть хотя бы одна ДОСТОВЕРНАЯ серия, подтверждающая рост:
        // старые отчёты формировались суммированием коротких/мёртвых серий и
        // давали ложные CRITICAL, которые нельзя переносить в сводку.
        var hasReliableSignal = report.Findings.Any(f =>
            f.Reliable && IsSignalMetric(f) && f.SlopePerHour > 0);

        if (!string.Equals(report.Verdict.Level, "OK", StringComparison.OrdinalIgnoreCase))
        {
            if (hasReliableSignal)
            {
                issues.Add(new AnalysisIssue
                {
                    Kind = nameof(IssueKind.Verdict),
                    Severity = report.Verdict.Level.ToUpperInvariant(),
                    Message = report.Verdict.Reason,
                    SlopePerHour = Math.Max(report.Verdict.HeapSlopeMbPerHour, report.Verdict.RssSlopeMbPerHour),
                    R2 = report.Verdict.HeapR2,
                    SourceFile = sourceFile,
                    GeneratedAt = sourceGeneratedAt
                });
            }
            else
            {
                issues.Add(new AnalysisIssue
                {
                    Kind = nameof(IssueKind.DataQuality),
                    Severity = "INFO",
                    Message = $"Вердикт отчёта '{report.Verdict.Level}' не подтверждён достоверными " +
                              "сериями (нет серий с достаточным числом точек/длительностью окна) — " +
                              "перенос в сводку отменён.",
                    SlopePerHour = Math.Max(report.Verdict.HeapSlopeMbPerHour, report.Verdict.RssSlopeMbPerHour),
                    R2 = report.Verdict.HeapR2,
                    SourceFile = sourceFile,
                    GeneratedAt = sourceGeneratedAt
                });
            }
        }

        // 2. Анализ каждой серии метрик.
        foreach (var f in report.Findings)
            AnalyzeFinding(f, issues, sourceFile, sourceGeneratedAt);

        // 3. Качество данных: слишком мало серий/точек.
        if (report.Findings.Count == 0)
        {
            issues.Add(new AnalysisIssue
            {
                Kind = nameof(IssueKind.DataQuality),
                Severity = "WARNING",
                Message = "Отчёт не содержит ни одной серии метрик — проверьте, что сервис отправлял runtime-метрики.",
                SourceFile = sourceFile,
                GeneratedAt = sourceGeneratedAt
            });
        }

        var instances = report.Findings
            .Select(f => f.Instance)
            .Where(i => !string.IsNullOrEmpty(i))
            .Distinct()
            .OrderBy(i => i, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ReportSummary
        {
            File = Path.GetFileName(file),
            GeneratedAt = report.GeneratedAt,
            PrometheusUrl = report.PrometheusUrl,
            WindowHours = report.WindowHours,
            Step = report.Step,
            Job = report.Job,
            VerdictLevel = report.Verdict.Level,
            VerdictReason = report.Verdict.Reason,
            HeapSlopeMbPerHour = report.Verdict.HeapSlopeMbPerHour,
            RssSlopeMbPerHour = report.Verdict.RssSlopeMbPerHour,
            HeapR2 = report.Verdict.HeapR2,
            FindingCount = report.Findings.Count,
            Instances = instances,
            Issues = issues
        };
    }

    /// <summary>Оценка одной серии метрики.</summary>
    private void AnalyzeFinding(
        MetricFinding f, List<AnalysisIssue> issues,
        string sourceFile, string sourceGeneratedAt)
    {
        var slope = double.IsFinite(f.SlopePerHour) ? f.SlopePerHour : 0.0;
        var r2 = double.IsFinite(f.R2) ? f.R2 : 0.0;

        // Серия достоверна, если фит не шумный И набрано достаточно точек/длительности.
        // Короткие стартовые серии (прогрев после перезапуска) дают высокий R² на
        // 7–8 точках и раньше приводили к ложным CRITICAL.
        var enoughData = f.Samples >= _options.MinSamples
                         && f.DurationSeconds >= _options.MinDurationMinutes * 60L;
        var reliable = r2 >= _options.MinR2 && enoughData;

        if (!enoughData && Math.Abs(slope) > _options.HeapWarnSlope)
        {
            issues.Add(new AnalysisIssue
            {
                Kind = nameof(IssueKind.DataQuality),
                Severity = "INFO",
                Metric = f.Metric,
                Instance = f.Instance,
                SlopePerHour = slope,
                R2 = r2,
                Message = $"Тренд '{f.Description}' пропущен как недостоверный: мало данных " +
                          $"({f.Samples} точек, {f.DurationSeconds / 60} мин; требуется ≥ {_options.MinSamples} " +
                          $"точек и ≥ {_options.MinDurationMinutes} мин).",
                SourceFile = sourceFile,
                GeneratedAt = sourceGeneratedAt
            });
            return;
        }

        // Шумный фит при заметном тренде — сообщаем о недостатке данных.
        if (!reliable && Math.Abs(slope) > _options.HeapWarnSlope)
        {
            issues.Add(new AnalysisIssue
            {
                Kind = nameof(IssueKind.DataQuality),
                Severity = "INFO",
                Metric = f.Metric,
                Instance = f.Instance,
                SlopePerHour = slope,
                R2 = r2,
                Message = $"Тренд '{f.Description}' не подтверждён: R²={r2:N2} < {_options.MinR2:N2}, " +
                          $"наклон {slope:N2} {Unit(f)}/ч может быть шумом.",
                SourceFile = sourceFile,
                GeneratedAt = sourceGeneratedAt
            });
        }

        switch (f.Metric)
        {
            case "managed_heap":
                AnalyzeGrowth(f, issues, Source(sourceFile, sourceGeneratedAt), IssueKind.Gen2Pressure,
                    _options.HeapWarnSlope, _options.HeapCriticalSlope, reliable);
                // Долгоживущие объекты: heap держится у максимума И при этом
                // наблюдается заметный рост. Плоская/падающая серия у максимума —
                // это нормальный рабочий цикл GC (heap ↔ RSS осциллируют), а не
                // утечка, поэтому WARNING без реального роста не выдаём.
                if (reliable && f.Max > 0 && f.Last >= f.Max * 0.95 && slope > _options.HeapWarnSlope / 2)
                {
                    issues.Add(new AnalysisIssue
                    {
                        Kind = nameof(IssueKind.Gen2Pressure),
                        Severity = "WARNING",
                        Metric = f.Metric,
                        Instance = f.Instance,
                        SlopePerHour = slope,
                        R2 = r2,
                        Message = $"Managed heap держится у максимума ({f.Last:N1}/{f.Max:N1} MB) при росте " +
                                  $"{slope:N2} MB/ч — вероятны долгоживущие (Gen2) объекты.",
                        SourceFile = sourceFile,
                        GeneratedAt = sourceGeneratedAt
                    });
                }
                break;

            case "rss":
                AnalyzeGrowth(f, issues, Source(sourceFile, sourceGeneratedAt), IssueKind.LeakTrend,
                    _options.RssWarnSlope, _options.RssCriticalSlope, reliable);
                break;

            // Размер heap по поколениям: рост Gen2/LOH — сильный признак утечки
            // долгоживущих объектов (в отличие от кратковременных Gen0/Gen1).
            case "heap_gen":
                var isLongLived = IsLongLivedGeneration(f.Generation);
                if (isLongLived)
                {
                    AnalyzeGrowth(f, issues, Source(sourceFile, sourceGeneratedAt), IssueKind.Gen2Pressure,
                        _options.Gen2WarnSlope, _options.Gen2CriticalSlope, reliable);
                }
                break;

            case "gc_fragmentation":
                if (reliable && slope > 1.0)
                {
                    issues.Add(new AnalysisIssue
                    {
                        Kind = nameof(IssueKind.Fragmentation),
                        Severity = slope > 5.0 ? "WARNING" : "INFO",
                        Metric = f.Metric,
                        Instance = f.Instance,
                        SlopePerHour = slope,
                        R2 = r2,
                        Message = $"Растёт фрагментация heap ({slope:N2} MB/ч) — возможна деградация аллокатора.",
                        SourceFile = sourceFile,
                        GeneratedAt = sourceGeneratedAt
                    });
                }
                break;

            case "gc_committed":
                // Committed heap обычно колеблется; устойчивый рост на фоне роста heap — тревожно.
                if (reliable && slope > _options.HeapWarnSlope)
                {
                    issues.Add(new AnalysisIssue
                    {
                        Kind = nameof(IssueKind.WatchTrend),
                        Severity = "INFO",
                        Metric = f.Metric,
                        Instance = f.Instance,
                        SlopePerHour = slope,
                        R2 = r2,
                        Message = $"Растёт закоммиченная память heap ({slope:N2} MB/ч) — наблюдать вместе с managed heap.",
                        SourceFile = sourceFile,
                        GeneratedAt = sourceGeneratedAt
                    });
                }
                break;

            case "alloc_rate":
                // Сама по себе скорость аллокаций — нормальный процесс; фиксируем только экстремумы.
                if (reliable && slope > 100.0)
                {
                    issues.Add(new AnalysisIssue
                    {
                        Kind = nameof(IssueKind.UnexpectedGrowth),
                        Severity = "WARNING",
                        Metric = f.Metric,
                        Instance = f.Instance,
                        SlopePerHour = slope,
                        R2 = r2,
                        Message = $"Аномально растёт скорость аллокаций ({slope:N2} MB/s/ч) — проверить горячие пути.",
                        SourceFile = sourceFile,
                        GeneratedAt = sourceGeneratedAt
                    });
                }
                break;

            // threads / gc_collections / gc_pause — рост допустим и порогов не имеют.
        }
    }

    /// <summary>Упаковка источника отчёта в named-конструктор проблемы.</summary>
    private static (string SourceFile, string GeneratedAt) Source(string file, string generatedAt) =>
        (file, generatedAt);

    /// <summary>Универсальная оценка роста метрики по порогам WARNING/CRITICAL.</summary>
    private static void AnalyzeGrowth(
        MetricFinding f, List<AnalysisIssue> issues,
        (string SourceFile, string GeneratedAt) source, IssueKind kind,
        double warn, double critical, bool reliable)
    {
        var slope = double.IsFinite(f.SlopePerHour) ? f.SlopePerHour : 0.0;
        var r2 = double.IsFinite(f.R2) ? f.R2 : 0.0;

        // Рост без статистической достоверности не эскалируем.
        if (!reliable || slope <= 0) return;

        if (slope > critical)
        {
            issues.Add(new AnalysisIssue
            {
                Kind = kind.ToString(),
                Severity = "CRITICAL",
                Metric = f.Metric,
                Instance = f.Instance,
                SlopePerHour = slope,
                R2 = r2,
                Message = $"Критичный устойчивый рост '{f.Description}': {slope:N2} {Unit(f)}/ч (R²={r2:N2}).",
                SourceFile = source.SourceFile,
                GeneratedAt = source.GeneratedAt
            });
        }
        else if (slope > warn)
        {
            issues.Add(new AnalysisIssue
            {
                Kind = kind.ToString(),
                Severity = "WARNING",
                Metric = f.Metric,
                Instance = f.Instance,
                SlopePerHour = slope,
                R2 = r2,
                Message = $"Устойчивый рост '{f.Description}': {slope:N2} {Unit(f)}/ч (R²={r2:N2}) — вероятная утечка.",
                SourceFile = source.SourceFile,
                GeneratedAt = source.GeneratedAt
            });
        }
        else if (slope > warn / 2)
        {
            issues.Add(new AnalysisIssue
            {
                Kind = nameof(IssueKind.WatchTrend),
                Severity = "INFO",
                Metric = f.Metric,
                Instance = f.Instance,
                SlopePerHour = slope,
                R2 = r2,
                Message = $"Слабый, но устойчивый рост '{f.Description}': {slope:N2} {Unit(f)}/ч (R²={r2:N2}) — на наблюдение.",
                SourceFile = source.SourceFile,
                GeneratedAt = source.GeneratedAt
            });
        }
    }

    private static string Unit(MetricFinding f) => f.IsBytes ? "MB" : f.Unit;

    /// <summary>
    /// Сигнальные метрики, рост которых подтверждает утечку: managed heap,
    /// RSS, размер долгоживущих поколений.
    /// </summary>
    private static bool IsSignalMetric(MetricFinding f) =>
        f.Metric is "managed_heap" or "rss"
        || (f.Metric == "heap_gen" && IsLongLivedGeneration(f.Generation));

    /// <summary>
    /// Определяет, относится ли метка generation к долгоживущим поколениям
    /// (Gen2 / LOH / Pinned), рост которых — признак утечки.
    /// </summary>
    private static bool IsLongLivedGeneration(string generation)
    {
        if (string.IsNullOrWhiteSpace(generation)) return false;
        var g = generation.ToLowerInvariant();
        return g.Contains("2") || g.Contains("loh") || g.Contains("pinned")
               || g.Contains("large"); // large object heap
    }

    /// <summary>Итоговый вердикт по всем отчётам и проблемам.</summary>
    private static void ComputeVerdict(AnalysisResult result)
    {
        if (result.ReportsAnalyzed == 0)
        {
            result.Verdict = "WARNING";
            result.Reason = "Не найдено ни одного JSON-отчёта для анализа.";
            return;
        }

        var critical = result.Reports.SelectMany(r => r.Issues).Count(i => i.SeverityEnum == IssueSeverity.Critical);
        var warnings = result.Reports.SelectMany(r => r.Issues).Count(i => i.SeverityEnum == IssueSeverity.Warning);

        if (critical > 0)
        {
            result.Verdict = "CRITICAL";
            result.Reason = $"Обнаружено критичных проблем: {critical} (предупреждений: {warnings}).";
        }
        else if (warnings > 0)
        {
            result.Verdict = "WARNING";
            result.Reason = $"Обнаружены предупреждения: {warnings}. Критичных проблем нет.";
        }
        else
        {
            result.Verdict = "OK";
            result.Reason = "Признаков утечки памяти не обнаружено ни в одном отчёте.";
        }
    }

    private static DateTime? TryParseTimestamp(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt)
            ? dt
            : null;
    }
}
