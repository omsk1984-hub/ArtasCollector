using System.Text.Json.Serialization;

namespace ArtasTools.ReportAnalyzer.Models;

/// <summary>Уровень опасности выявленной проблемы.</summary>
public enum IssueSeverity
{
    Info,
    Warning,
    Critical
}

/// <summary>Категория выявленной проблемы.</summary>
public enum IssueKind
{
    /// <summary>Устойчивый рост памяти (heap/RSS).</summary>
    LeakTrend,
    /// <summary>Устойчивый рост без превышения порогов — на наблюдение.</summary>
    WatchTrend,
    /// <summary>Рост метрики, которая по контракту расти не должна.</summary>
    UnexpectedGrowth,
    /// <summary>Долгоживущие объекты: heap не снижается после сборок.</summary>
    Gen2Pressure,
    /// <summary>Фрагментация heap.</summary>
    Fragmentation,
    /// <summary>Проблемы с данными отчёта (пусто, мало точек, шумный фит).</summary>
    DataQuality,
    /// <summary>Вердикт самого отчёта (OK/WARNING/CRITICAL).</summary>
    Verdict
}

/// <summary>Одна выявленная при анализе проблема.</summary>
public sealed class AnalysisIssue
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("severity")] public string Severity { get; set; } = "INFO";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("metric")] public string Metric { get; set; } = "";
    [JsonPropertyName("instance")] public string Instance { get; set; } = "";
    [JsonPropertyName("slopePerHour")] public double SlopePerHour { get; set; }
    [JsonPropertyName("r2")] public double R2 { get; set; }

    /// <summary>
    /// Файл исходного отчёта MemReport, из которого получена проблема.
    /// Позволяет отличить свежий сигнал от устаревшего (мёртвый инстанс).
    /// </summary>
    [JsonPropertyName("sourceFile")] public string SourceFile { get; set; } = "";

    /// <summary>Момент формирования исходного отчёта (UTC, ISO-8601).</summary>
    [JsonPropertyName("generatedAt")] public string GeneratedAt { get; set; } = "";

    [JsonIgnore] public IssueSeverity SeverityEnum => Severity switch
    {
        "CRITICAL" => IssueSeverity.Critical,
        "WARNING" => IssueSeverity.Warning,
        _ => IssueSeverity.Info
    };
}

/// <summary>Свод по одному отчёту MemReport.</summary>
public sealed class ReportSummary
{
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("generatedAt")] public string GeneratedAt { get; set; } = "";
    [JsonPropertyName("prometheusUrl")] public string PrometheusUrl { get; set; } = "";
    [JsonPropertyName("windowHours")] public int WindowHours { get; set; }
    [JsonPropertyName("step")] public string Step { get; set; } = "";
    [JsonPropertyName("job")] public string Job { get; set; } = "";
    [JsonPropertyName("verdictLevel")] public string VerdictLevel { get; set; } = "OK";
    [JsonPropertyName("verdictReason")] public string VerdictReason { get; set; } = "";
    [JsonPropertyName("heapSlopeMbPerHour")] public double HeapSlopeMbPerHour { get; set; }
    [JsonPropertyName("rssSlopeMbPerHour")] public double RssSlopeMbPerHour { get; set; }
    [JsonPropertyName("heapR2")] public double HeapR2 { get; set; }
    [JsonPropertyName("findingCount")] public int FindingCount { get; set; }
    [JsonPropertyName("instances")] public List<string> Instances { get; set; } = new();
    [JsonPropertyName("issues")] public List<AnalysisIssue> Issues { get; set; } = new();

    /// <summary>Максимальная серьёзность среди issues (для сводной сортировки).</summary>
    [JsonIgnore] public IssueSeverity MaxSeverity =>
        Issues.Count == 0 ? IssueSeverity.Info : Issues.Max(i => i.SeverityEnum);
}

/// <summary>Полный результат анализа каталога отчётов.</summary>
public sealed class AnalysisResult
{
    [JsonPropertyName("generatedAt")] public string GeneratedAt { get; set; } = "";
    [JsonPropertyName("reportsDir")] public string ReportsDir { get; set; } = "";
    [JsonPropertyName("reportsAnalyzed")] public int ReportsAnalyzed { get; set; }
    [JsonPropertyName("verdict")] public string Verdict { get; set; } = "OK";
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";
    [JsonPropertyName("reports")] public List<ReportSummary> Reports { get; set; } = new();

    /// <summary>Агрегированные проблемы по всем отчётам (без DataQuality-шума).</summary>
    [JsonPropertyName("topIssues")] public List<AnalysisIssue> TopIssues { get; set; } = new();

    [JsonIgnore] public IssueSeverity VerdictEnum => Verdict switch
    {
        "CRITICAL" => IssueSeverity.Critical,
        "WARNING" => IssueSeverity.Warning,
        _ => IssueSeverity.Info
    };
}
