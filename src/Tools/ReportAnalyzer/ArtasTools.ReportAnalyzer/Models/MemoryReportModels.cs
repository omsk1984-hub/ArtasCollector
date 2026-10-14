using System.Text.Json.Serialization;

namespace ArtasTools.ReportAnalyzer.Models;

/// <summary>
/// Серия метрики с рассчитанным трендом — элемент раздела <c>findings</c>
/// JSON-отчёта, формируемого ArtasTools.MemReport. Контракт читается
/// «как есть», чтобы анализатор был совместим с уже сгенерированными отчётами.
/// </summary>
public sealed class MetricFinding
{
    [JsonPropertyName("metric")] public string Metric { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("unit")] public string Unit { get; set; } = "";
    [JsonPropertyName("generation")] public string Generation { get; set; } = "";
    [JsonPropertyName("instance")] public string Instance { get; set; } = "";
    [JsonPropertyName("samples")] public int Samples { get; set; }
    [JsonPropertyName("first")] public double First { get; set; }
    [JsonPropertyName("last")] public double Last { get; set; }
    [JsonPropertyName("min")] public double Min { get; set; }
    [JsonPropertyName("max")] public double Max { get; set; }
    [JsonPropertyName("slopePerHour")] public double SlopePerHour { get; set; }
    [JsonPropertyName("r2")] public double R2 { get; set; }
    [JsonPropertyName("isBytes")] public bool IsBytes { get; set; }

    [JsonPropertyName("firstTimestamp")] public long FirstTimestamp { get; set; }
    [JsonPropertyName("lastTimestamp")] public long LastTimestamp { get; set; }
    [JsonPropertyName("durationSeconds")] public long DurationSeconds { get; set; }
    [JsonPropertyName("baselineSlopePerHour")] public double BaselineSlopePerHour { get; set; }
    [JsonPropertyName("baselineR2")] public double BaselineR2 { get; set; }
    [JsonPropertyName("baselineSamples")] public int BaselineSamples { get; set; }
    [JsonPropertyName("reliable")] public bool Reliable { get; set; }
}

/// <summary>Итоговая оценка утечки из JSON-отчёта.</summary>
public sealed class LeakVerdict
{
    [JsonPropertyName("level")] public string Level { get; set; } = "OK";
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";
    [JsonPropertyName("heapSlopeMbPerHour")] public double HeapSlopeMbPerHour { get; set; }
    [JsonPropertyName("rssSlopeMbPerHour")] public double RssSlopeMbPerHour { get; set; }
    [JsonPropertyName("heapR2")] public double HeapR2 { get; set; }
}

/// <summary>
/// Полный JSON-отчёт MemReport. Поля соответствуют сериализации
/// <c>ArtasTools.MemReport.Models.MemoryReport</c>.
/// </summary>
public sealed class MemoryReport
{
    [JsonPropertyName("generatedAt")] public string GeneratedAt { get; set; } = "";
    [JsonPropertyName("prometheusUrl")] public string PrometheusUrl { get; set; } = "";
    [JsonPropertyName("windowHours")] public int WindowHours { get; set; }
    [JsonPropertyName("step")] public string Step { get; set; } = "";
    [JsonPropertyName("job")] public string Job { get; set; } = "";
    [JsonPropertyName("verdict")] public LeakVerdict Verdict { get; set; } = new();
    [JsonPropertyName("findings")] public List<MetricFinding> Findings { get; set; } = new();
}
