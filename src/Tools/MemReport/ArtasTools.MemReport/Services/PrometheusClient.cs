using System.Net;
using System.Text.Json;
using ArtasTools.MemReport.Models;

namespace ArtasTools.MemReport.Services;

/// <summary>
/// Клиент HTTP API Prometheus (instant/range запросы к TSDB, куда OTel
/// Collector пишет .NET runtime-метрики через remote-write).
/// </summary>
public sealed class PrometheusClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public PrometheusClient(string baseUrl, TimeSpan? timeout = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(15) };
    }

    /// <summary>Проверка доступности Prometheus (запрос up).</summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await QueryInstantAsync("up", ct);
            return resp is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Полный список имён метрик (полезно для диагностики).</summary>
    public async Task<IReadOnlyList<string>> GetMetricNamesAsync(CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/api/v1/label/__name__/values";
        try
        {
            using var doc = await GetJsonAsync(url, ct);
            if (doc is null) return Array.Empty<string>();
            if (!doc.RootElement.TryGetProperty("data", out var data)) return Array.Empty<string>();
            var list = new List<string>();
            foreach (var el in data.EnumerateArray())
            {
                var name = el.GetString();
                if (!string.IsNullOrEmpty(name)) list.Add(name);
            }
            return list;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Мгновенный запрос (instant vector).</summary>
    public async Task<PromResponse<PromInstantSample>?> QueryInstantAsync(string query, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/api/v1/query?query={Uri.EscapeDataString(query)}";
        return await GetAsync<PromResponse<PromInstantSample>>(url, ct);
    }

    /// <summary>Range-запрос (matrix) за последние <paramref name="hours"/> часов.</summary>
    public async Task<PromResponse<PromRangeSample>?> QueryRangeAsync(
        string query, int hours, string step, CancellationToken ct = default)
    {
        var end = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var start = end - (long)hours * 3600;
        var url = $"{_baseUrl}/api/v1/query_range" +
                  $"?query={Uri.EscapeDataString(query)}" +
                  $"&start={start}&end={end}&step={Uri.EscapeDataString(step)}";
        return await GetAsync<PromResponse<PromRangeSample>>(url, ct);
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
    {
        var json = await GetRawAsync(url, ct);
        if (json is null) return default;
        return JsonSerializer.Deserialize<T>(json, JsonOpts);
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct)
    {
        var json = await GetRawAsync(url, ct);
        return json is null ? null : JsonDocument.Parse(json);
    }

    private async Task<string?> GetRawAsync(string url, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(url, ct);
            if (resp.StatusCode != HttpStatusCode.OK)
            {
                Console.Error.WriteLine($"[WARN] HTTP {(int)resp.StatusCode} для {url}");
                return null;
            }
            return await resp.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WARN] Запрос не удался: {url} — {ex.Message}");
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}
