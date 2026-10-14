using System.Text.Json.Serialization;

namespace ArtasTools.MemReport.Models;

/// <summary>Обёртка ответа Prometheus HTTP API.</summary>
public sealed class PromResponse<T>
{
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("data")] public PromData<T>? Data { get; set; }
}

/// <summary>Секция data ответа Prometheus.</summary>
public sealed class PromData<T>
{
    [JsonPropertyName("resultType")] public string ResultType { get; set; } = "";
    [JsonPropertyName("result")] public List<T> Result { get; set; } = new();
}

/// <summary>Результат instant-запроса (вектор).</summary>
public sealed class PromInstantSample
{
    [JsonPropertyName("metric")] public Dictionary<string, string> Metric { get; set; } = new();
    [JsonPropertyName("value")] public List<JsonElementRaw> Value { get; set; } = new();
}

/// <summary>Результат range-запроса (матрица).</summary>
public sealed class PromRangeSample
{
    [JsonPropertyName("metric")] public Dictionary<string, string> Metric { get; set; } = new();
    [JsonPropertyName("values")] public List<List<JsonElementRaw>> Values { get; set; } = new();
}

/// <summary>
/// Элемент пары [timestamp, value] из Prometheus. Значения приходят
/// смешанными типами (number для ts, string для value), поэтому читаем
/// как сырой JSON и интерпретируем вручную.
/// </summary>
[JsonConverter(typeof(JsonElementRawConverter))]
public sealed class JsonElementRaw
{
    public string Raw { get; set; } = "";

    /// <summary>Число как double (значение метрики).</summary>
    public double AsDouble() =>
        double.TryParse(Raw, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0.0;

    /// <summary>Число как long (timestamp в секундах, может быть float).</summary>
    public long AsLong() =>
        (long)Math.Round(AsDouble(), MidpointRounding.AwayFromZero);

    public override string ToString() => Raw;
}
