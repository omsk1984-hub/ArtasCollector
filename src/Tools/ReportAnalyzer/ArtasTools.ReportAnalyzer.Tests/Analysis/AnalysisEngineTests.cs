using ArtasTools.ReportAnalyzer.Analysis;
using ArtasTools.ReportAnalyzer.Models;
using ArtasTools.ReportAnalyzer.Options;
using Xunit;

namespace ArtasTools.ReportAnalyzer.Tests.Analysis;

public class AnalysisEngineTests
{
    private static MemoryReport ReportWith(params MetricFinding[] findings) => new()
    {
        GeneratedAt = "2026-10-12T06:46:25Z",
        PrometheusUrl = "http://localhost:9090",
        WindowHours = 6,
        Step = "60s",
        Job = "artas-writer",
        Verdict = new LeakVerdict
        {
            Level = "OK",
            Reason = "Признаков устойчивого роста памяти не обнаружено.",
            HeapSlopeMbPerHour = 0.7,
            RssSlopeMbPerHour = -18.3,
            HeapR2 = 0.7
        },
        Findings = findings.ToList()
    };

    private static MetricFinding Finding(
        string metric, double slope, double r2, bool isBytes = true,
        double first = 1, double last = 1, double min = 0, double max = 2,
        string instance = "inst-1", string unit = "MB",
        int samples = 43, long durationSeconds = 7200, string generation = "") => new()
    {
        Metric = metric,
        Description = metric,
        Unit = unit,
        Instance = instance,
        Samples = samples,
        DurationSeconds = durationSeconds,
        Generation = generation,
        First = first,
        Last = last,
        Min = min,
        Max = max,
        SlopePerHour = slope,
        R2 = r2,
        IsBytes = isBytes,
        Reliable = samples >= 30 && durationSeconds >= 3600
    };

    [Fact]
    public void AnalyzeReport_NoIssues_WhenNoGrowth()
    {
        var engine = new AnalysisEngine(new AnalyzerOptions());
        var summary = engine.AnalyzeReport("r.json", ReportWith(
            Finding("managed_heap", slope: -1.4, r2: 0.02),
            Finding("rss", slope: -18.3, r2: 0.43)));

        Assert.Equal("OK", summary.VerdictLevel);
        Assert.DoesNotContain(summary.Issues, i => i.SeverityEnum == IssueSeverity.Warning);
        Assert.DoesNotContain(summary.Issues, i => i.SeverityEnum == IssueSeverity.Critical);
    }

    [Fact]
    public void AnalyzeReport_CriticalHeapGrowth_ProducesCriticalIssue()
    {
        var engine = new AnalysisEngine(new AnalyzerOptions());
        var summary = engine.AnalyzeReport("r.json", ReportWith(
            Finding("managed_heap", slope: 60.0, r2: 0.9)));

        Assert.Contains(summary.Issues,
            i => i.SeverityEnum == IssueSeverity.Critical && i.Kind == nameof(IssueKind.Gen2Pressure));
    }

    [Fact]
    public void AnalyzeReport_WarningRssGrowth_ProducesWarningIssue()
    {
        var engine = new AnalysisEngine(new AnalyzerOptions());
        var summary = engine.AnalyzeReport("r.json", ReportWith(
            Finding("rss", slope: 25.0, r2: 0.8)));

        Assert.Contains(summary.Issues,
            i => i.SeverityEnum == IssueSeverity.Warning && i.Metric == "rss");
    }

    [Fact]
    public void AnalyzeReport_NoisyHighSlope_NotEscalated_ButDataQuality()
    {
        // Высокий наклон, но R² ниже порога — не эскалируем, но помечаем качество данных.
        var engine = new AnalysisEngine(new AnalyzerOptions());
        var summary = engine.AnalyzeReport("r.json", ReportWith(
            Finding("managed_heap", slope: 30.0, r2: 0.1)));

        Assert.DoesNotContain(summary.Issues, i => i.SeverityEnum == IssueSeverity.Critical);
        Assert.DoesNotContain(summary.Issues, i => i.SeverityEnum == IssueSeverity.Warning);
        Assert.Contains(summary.Issues, i => i.Kind == nameof(IssueKind.DataQuality));
    }

    [Fact]
    public void AnalyzeReport_HeapAtMaximum_WithGrowth_FlagsGen2Pressure()
    {
        // Heap держится у максимума И растёт — признак долгоживущих объектов.
        var engine = new AnalysisEngine(new AnalyzerOptions());
        var summary = engine.AnalyzeReport("r.json", ReportWith(
            Finding("managed_heap", slope: 12.0, r2: 0.8,
                first: 7.9, last: 7.9, min: 5.9, max: 7.9)));

        Assert.Contains(summary.Issues, i =>
            i.Kind == nameof(IssueKind.Gen2Pressure) && i.SeverityEnum == IssueSeverity.Warning);
    }

    [Fact]
    public void AnalyzeReport_HeapAtMaximum_Flat_NoFalseGen2Pressure()
    {
        // Регрессия: плоская/падающая серия у максимума — нормальный цикл GC,
        // а не утечка. WARNING «heap у максимума» без реального роста не выдаём.
        var engine = new AnalysisEngine(new AnalyzerOptions());
        var summary = engine.AnalyzeReport("r.json", ReportWith(
            Finding("managed_heap", slope: 0.0, r2: 0.8,
                first: 7.9, last: 7.9, min: 5.9, max: 7.9)));

        Assert.DoesNotContain(summary.Issues,
            i => i.Kind == nameof(IssueKind.Gen2Pressure) && i.SeverityEnum == IssueSeverity.Warning);
    }

    [Fact]
    public void AnalyzeReport_Issues_CarrySourceFileAndDate()
    {
        // Каждая проблема должна нести источник (файл + дату отчёта), чтобы в
        // сводке было видно, к какому моменту времени она относится.
        var engine = new AnalysisEngine(new AnalyzerOptions());
        var summary = engine.AnalyzeReport("memory_leak_report_2026-10-13_09-52-28.json", ReportWith(
            Finding("managed_heap", slope: 12.0, r2: 0.8,
                first: 7.9, last: 7.9, min: 5.9, max: 7.9)));

        var issue = summary.Issues.First(i => i.Kind == nameof(IssueKind.Gen2Pressure));
        Assert.Equal("memory_leak_report_2026-10-13_09-52-28.json", issue.SourceFile);
        Assert.Equal("2026-10-12T06:46:25Z", issue.GeneratedAt);
    }

    [Fact]
    public void AnalyzeReport_ShortSeries_HighSlope_NotEscalated()
    {
        // Регрессия: серия из 7 точек (стартовый прогрев перезапущенного процесса)
        // с высоким наклоном и высоким R² не должна давать WARNING/CRITICAL.
        var engine = new AnalysisEngine(new AnalyzerOptions());
        var summary = engine.AnalyzeReport("r.json", ReportWith(
            Finding("managed_heap", slope: 60.0, r2: 0.9, samples: 7, durationSeconds: 360)));

        Assert.DoesNotContain(summary.Issues, i => i.SeverityEnum == IssueSeverity.Critical);
        Assert.DoesNotContain(summary.Issues, i => i.SeverityEnum == IssueSeverity.Warning);
    }

    [Fact]
    public void AnalyzeReport_Gen2HeapGrowth_FlagsGen2Pressure()
    {
        var engine = new AnalysisEngine(new AnalyzerOptions());
        var summary = engine.AnalyzeReport("r.json", ReportWith(
            Finding("heap_gen", slope: 10.0, r2: 0.9, generation: "generation:2")));

        Assert.Contains(summary.Issues,
            i => i.SeverityEnum == IssueSeverity.Warning && i.Kind == nameof(IssueKind.Gen2Pressure));
    }

    [Fact]
    public void AnalyzeReport_Gen0HeapGrowth_Ignored()
    {
        // Рост Gen0 — норма (кратковременные объекты), не эскалируем.
        var engine = new AnalysisEngine(new AnalyzerOptions());
        var summary = engine.AnalyzeReport("r.json", ReportWith(
            Finding("heap_gen", slope: 10.0, r2: 0.9, generation: "generation:0")));

        Assert.DoesNotContain(summary.Issues, i => i.Kind == nameof(IssueKind.Gen2Pressure));
    }

    [Fact]
    public void AnalyzeReport_FragmentationGrowth_FlagsIssue()
    {
        var engine = new AnalysisEngine(new AnalyzerOptions());
        var summary = engine.AnalyzeReport("r.json", ReportWith(
            Finding("gc_fragmentation", slope: 2.0, r2: 0.7)));

        Assert.Contains(summary.Issues, i => i.Kind == nameof(IssueKind.Fragmentation));
    }

    [Fact]
    public void AnalyzeReport_NonOkVerdict_PropagatedAsIssue()
    {
        var report = ReportWith(Finding("managed_heap", slope: 5.0, r2: 0.9));
        report.Verdict.Level = "WARNING";
        report.Verdict.Reason = "Заметный рост.";
        report.Verdict.HeapR2 = 0.9;

        var engine = new AnalysisEngine(new AnalyzerOptions());
        var summary = engine.AnalyzeReport("r.json", report);

        Assert.Contains(summary.Issues,
            i => i.Kind == nameof(IssueKind.Verdict) && i.Severity == "WARNING");
    }

    [Fact]
    public void AnalyzeReport_EmptyFindings_FlagsDataQuality()
    {
        var engine = new AnalysisEngine(new AnalyzerOptions());
        var summary = engine.AnalyzeReport("r.json", ReportWith());

        Assert.Contains(summary.Issues, i => i.Kind == nameof(IssueKind.DataQuality));
    }

    [Fact]
    public async Task AnalyzeAsync_MissingDirectory_ReturnsWarning()
    {
        var engine = new AnalysisEngine(new AnalyzerOptions());
        var result = await engine.AnalyzeAsync(Path.Combine(Path.GetTempPath(), "no_such_dir_" + Guid.NewGuid()));

        Assert.Equal("WARNING", result.Verdict);
        Assert.Equal(0, result.ReportsAnalyzed);
    }

    [Fact]
    public async Task AnalyzeAsync_ReadsReportsAndComputesVerdict()
    {
        var dir = Directory.CreateTempSubdirectory("artas_analyzer_").FullName;
        try
        {
            var report = ReportWith(Finding("managed_heap", slope: 60.0, r2: 0.9,
                first: 1, last: 8, min: 1, max: 8));
            var json = System.Text.Json.JsonSerializer.Serialize(report);
            await File.WriteAllTextAsync(Path.Combine(dir, "memory_leak_report_2026-10-12_09-46-25.json"), json);

            var engine = new AnalysisEngine(new AnalyzerOptions());
            var result = await engine.AnalyzeAsync(dir);

            Assert.Equal(1, result.ReportsAnalyzed);
            Assert.Equal("CRITICAL", result.Verdict);
            Assert.NotEmpty(result.TopIssues);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
