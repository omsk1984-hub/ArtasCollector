using System.Text.Json;
using ArtasTools.MemReport.Models;
using Xunit;

namespace ArtasTools.MemReport.Tests.Models;

public class PromModelsTests
{
    [Fact]
    public void Deserialize_RangeResponse_ParsesValues()
    {
        // Формат ответа Prometheus /api/v1/query_range.
        const string json = """
        {
          "status": "success",
          "data": {
            "resultType": "matrix",
            "result": [
              {
                "metric": { "generation": "gen2", "service_name": "artas-writer" },
                "values": [
                  [1700000000, "1024"],
                  [1700000060, "2048"]
                ]
              }
            ]
          }
        }
        """;

        var resp = JsonSerializer.Deserialize<PromResponse<PromRangeSample>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(resp);
        Assert.Equal("success", resp!.Status);
        Assert.Single(resp.Data!.Result);
        var series = resp.Data.Result[0];
        Assert.Equal("gen2", series.Metric["generation"]);
        Assert.Equal(2, series.Values.Count);
        Assert.Equal(1700000000, series.Values[0][0].AsLong());
        Assert.Equal(1024, series.Values[0][1].AsDouble(), 3);
        Assert.Equal(2048, series.Values[1][1].AsDouble(), 3);
    }

    [Fact]
    public void JsonElementRaw_ParsesStringNumber()
    {
        var raw = new JsonElementRaw { Raw = "12345.67" };
        Assert.Equal(12345.67, raw.AsDouble(), 2);
        Assert.Equal(12346, raw.AsLong());
    }

    [Fact]
    public void Deserialize_InstantResponse_ParsesValue()
    {
        const string json = """
        {
          "status": "success",
          "data": {
            "resultType": "vector",
            "result": [
              { "metric": { "job": "prometheus" }, "value": [1700000000, "1"] }
            ]
          }
        }
        """;

        var resp = JsonSerializer.Deserialize<PromResponse<PromInstantSample>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(resp);
        Assert.Single(resp!.Data!.Result);
        Assert.Equal(1, resp.Data.Result[0].Value[1].AsDouble(), 3);
    }
}
