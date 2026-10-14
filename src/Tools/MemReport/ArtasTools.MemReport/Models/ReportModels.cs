using System.Text.Json.Serialization;

namespace ArtasTools.MemReport.Models;

/// <summary>Уровень итоговой оценки утечки памяти.</summary>
public enum LeakLevel
{
    Ok,
    Warning,
    Critical
}

/// <summary>Итоговая оценка вероятности утечки.</summary>
public sealed class LeakVerdict
{
    [JsonPropertyName("level")] public string Level { get; set; } = "OK";
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";
    [JsonPropertyName("heapSlopeMbPerHour")] public double HeapSlopeMbPerHour { get; set; }
    [JsonPropertyName("rssSlopeMbPerHour")] public double RssSlopeMbPerHour { get; set; }
    [JsonPropertyName("heapR2")] public double HeapR2 { get; set; }

    [JsonIgnore] public LeakLevel LevelEnum => Level switch
    {
        "CRITICAL" => LeakLevel.Critical,
        "WARNING" => LeakLevel.Warning,
        _ => LeakLevel.Ok
    };
}

/// <summary>Найденная серия метрики с рассчитанным трендом.</summary>
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

    /// <summary>Метка времени первой точки серии, unix-секунды.</summary>
    [JsonPropertyName("firstTimestamp")] public long FirstTimestamp { get; set; }

    /// <summary>Метка времени последней точки серии, unix-секунды.</summary>
    [JsonPropertyName("lastTimestamp")] public long LastTimestamp { get; set; }

    /// <summary>Длительность наблюдения серии, секунды.</summary>
    [JsonPropertyName("durationSeconds")] public long DurationSeconds { get; set; }

    /// <summary>Тренд нижней огибающей (post-GC минимумы), МБ/ч (или собственные единицы).</summary>
    [JsonPropertyName("baselineSlopePerHour")] public double BaselineSlopePerHour { get; set; }

    /// <summary>R² фита по нижней огибающей.</summary>
    [JsonPropertyName("baselineR2")] public double BaselineR2 { get; set; }

    /// <summary>Число точек нижней огибающей (post-GC минимумы).</summary>
    [JsonPropertyName("baselineSamples")] public int BaselineSamples { get; set; }

    /// <summary>Достаточно ли длинная серия, чтобы тренду доверять.</summary>
    [JsonPropertyName("reliable")] public bool Reliable { get; set; }
}

/// <summary>Полный отчёт.</summary>
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
