using System.Text.Json;
using ArtasTools.MemReport.Models;
using ArtasTools.MemReport.Reporting;
using Xunit;

namespace ArtasTools.MemReport.Tests.Reporting;

public class ReportRendererTests
{
    private static MemoryReport SampleReport() => new()
    {
        GeneratedAt = "2026-01-01T00:00:00Z",
        PrometheusUrl = "http://localhost:9090",
        WindowHours = 6,
        Step = "60s",
        Job = "artas-writer",
        Verdict = new LeakVerdict
        {
            Level = "WARNING",
            Reason = "Заметный рост.",
            HeapSlopeMbPerHour = 25.5,
            RssSlopeMbPerHour = 30.0,
            HeapR2 = 0.91
        },
        Findings = new List<MetricFinding>
        {
            new()
            {
                Metric = "managed_heap", Description = "Managed heap", Unit = "MB",
                Generation = "gen2", Samples = 360, First = 100, Last = 250,
                Min = 100, Max = 250, SlopePerHour = 25.5, BaselineSlopePerHour = 20.0,
                R2 = 0.91, IsBytes = true, DurationSeconds = 21600, Reliable = true
            },
            new()
            {
                Metric = "threads", Description = "Thread pool thread count", Unit = "шт",
                Generation = "", Samples = 30, First = 4, Last = 3,
                Min = 3, Max = 5, SlopePerHour = -0.5, R2 = 0.05, IsBytes = false,
                DurationSeconds = 1800, Reliable = false
            }
        }
    };

    [Fact]
    public void ToMarkdown_ContainsVerdictAndMetrics()
    {
        var md = ReportRenderer.ToMarkdown(SampleReport());

        Assert.Contains("WARNING", md);
        Assert.Contains("Managed heap", md);
        Assert.Contains("gen2", md);
        // Число форматируется текущей локалью (запятая/точка) — проверяем значение без учёта разделителя.
        Assert.Matches(@"25[.,]5", md);
    }

    [Fact]
    public void ToJson_IsValidAndRoundTrips()
    {
        var json = ReportRenderer.ToJson(SampleReport());

        var parsed = JsonSerializer.Deserialize<MemoryReport>(json);
        Assert.NotNull(parsed);
        Assert.Equal("WARNING", parsed!.Verdict.Level);
        Assert.Equal(2, parsed.Findings.Count);
        Assert.Equal("managed_heap", parsed.Findings[0].Metric);
        Assert.Equal(25.5, parsed.Findings[0].SlopePerHour, 3);
    }

    [Fact]
    public void ToMarkdown_NonByteMetric_ShowsRawUnits()
    {
        var md = ReportRenderer.ToMarkdown(SampleReport());

        // Thread count не должен рендериться как MB — единица «шт» и знак сохраняются.
        Assert.Contains("Thread pool thread count", md);
        Assert.Matches(@"4[.,]0 шт", md);
        Assert.Matches(@"-0[.,]50 шт/ч", md);
        // В строке threads нет единицы MB.
        var threadsRow = md.Split('\n').Single(l => l.Contains("Thread pool thread count"));
        Assert.DoesNotContain("MB", threadsRow);
    }

    [Fact]
    public void ToMarkdown_NoFindings_ShowsHint()
    {
        var report = SampleReport();
        report.Findings.Clear();

        var md = ReportRenderer.ToMarkdown(report);

        Assert.Contains("Метрики не найдены", md);
    }
}
