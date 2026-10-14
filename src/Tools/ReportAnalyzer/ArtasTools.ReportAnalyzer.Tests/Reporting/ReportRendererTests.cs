using System.Text.Json;
using ArtasTools.ReportAnalyzer.Models;
using ArtasTools.ReportAnalyzer.Reporting;
using Xunit;

namespace ArtasTools.ReportAnalyzer.Tests.Reporting;

public class ReportRendererTests
{
    private static AnalysisResult SampleResult() => new()
    {
        GeneratedAt = "2026-10-12T06:46:25Z",
        ReportsDir = "D:/work/Artas2026/reports",
        ReportsAnalyzed = 2,
        Verdict = "WARNING",
        Reason = "Обнаружены предупреждения: 1.",
        Reports = new List<ReportSummary>
        {
            new()
            {
                File = "memory_leak_report_a.json",
                GeneratedAt = "2026-10-12T06:46:25Z",
                Job = "artas-writer",
                VerdictLevel = "WARNING",
                HeapSlopeMbPerHour = 25.5,
                RssSlopeMbPerHour = 3.0,
                HeapR2 = 0.91,
                FindingCount = 4,
                Issues = new List<AnalysisIssue>
                {
                    new()
                    {
                        Kind = "Gen2Pressure", Severity = "WARNING", Metric = "managed_heap",
                        SlopePerHour = 25.5, R2 = 0.91,
                        Message = "Устойчивый рост managed heap."
                    }
                }
            },
            new()
            {
                File = "memory_leak_report_b.json",
                GeneratedAt = "2026-10-01T00:00:00Z",
                Job = "artas-writer",
                VerdictLevel = "OK",
                HeapSlopeMbPerHour = 0.1,
                RssSlopeMbPerHour = -0.2,
                HeapR2 = 0.3,
                FindingCount = 4
            }
        },
        TopIssues = new List<AnalysisIssue>
        {
            new()
            {
                Kind = "Gen2Pressure", Severity = "WARNING", Metric = "managed_heap",
                SlopePerHour = 25.5, R2 = 0.91,
                Message = "Устойчивый рост managed heap.",
                SourceFile = "memory_leak_report_a.json",
                GeneratedAt = "2026-10-12T06:46:25Z"
            }
        }
    };

    [Fact]
    public void ToMarkdown_ContainsVerdictAndReports()
    {
        var md = ReportRenderer.ToMarkdown(SampleResult());

        Assert.Contains("WARNING", md);
        Assert.Contains("memory_leak_report_a.json", md);
        Assert.Contains("Устойчивый рост managed heap.", md);
        Assert.Matches(@"25[.,]5", md);
    }

    [Fact]
    public void ToMarkdown_NoIssues_ShowsPlaceholder()
    {
        var result = SampleResult();
        result.TopIssues.Clear();
        result.Reports.Clear();

        var md = ReportRenderer.ToMarkdown(result);

        Assert.Contains("Проблем не обнаружено", md);
    }

    [Fact]
    public void ToJson_IsValidAndRoundTrips()
    {
        var json = ReportRenderer.ToJson(SampleResult());

        var parsed = JsonSerializer.Deserialize<AnalysisResult>(json);
        Assert.NotNull(parsed);
        Assert.Equal("WARNING", parsed!.Verdict);
        Assert.Equal(2, parsed.ReportsAnalyzed);
        Assert.Single(parsed.Reports[0].Issues);
        Assert.Equal(25.5, parsed.Reports[0].HeapSlopeMbPerHour, 3);
    }

    [Fact]
    public void ToMarkdown_ContainsIssueSource()
    {
        var md = ReportRenderer.ToMarkdown(SampleResult());

        Assert.Contains("Источник", md);
        Assert.Contains("memory_leak_report_a.json", md);
        Assert.Contains("2026-10-12T06:46:25Z", md);
    }

    [Fact]
    public void ToMarkdown_EscapesPipeInMessages()
    {
        var result = SampleResult();
        result.Reports.Clear();
        result.TopIssues.Add(new AnalysisIssue
        {
            Kind = "DataQuality", Severity = "INFO", Metric = "managed_heap",
            Message = "a | b"
        });

        var md = ReportRenderer.ToMarkdown(result);

        Assert.Contains(@"a \| b", md);
    }
}
