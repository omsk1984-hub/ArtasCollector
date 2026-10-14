using System.Text.Json.Serialization;
using ArtasCore.Json;
using ArtasCore.Models;

namespace ArtasCore.Data;

/// <summary>
/// DTO для JSONB-поля <c>metadata</c> таблицы <c>fractals</c>.
/// Соответствует структуре, которую пишет Python-сервис (fractal_writer),
/// а также используется Reader при чтении.
/// </summary>
public sealed class FractalMetadataDto
{
    [JsonPropertyName("is_up")] public bool IsUp { get; set; } = true;
    [JsonPropertyName("distance")] public double Distance { get; set; }
    [JsonPropertyName("min")] public double Min { get; set; }
    [JsonPropertyName("max")] public double Max { get; set; }

    [JsonPropertyName("def_interest")]
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long DefInterest { get; set; }

    [JsonPropertyName("from_start_interest")]
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long FromStartInterest { get; set; }

    [JsonPropertyName("peak")] public MyTick? Peak { get; set; }
    [JsonPropertyName("info")] public InfoF? Info { get; set; }

    /// <summary>Построить DTO из фрактала.</summary>
    public static FractalMetadataDto FromFractal(Fractal fractal)
    {
        ArgumentNullException.ThrowIfNull(fractal);

        return new FractalMetadataDto
        {
            IsUp = fractal.IsUp,
            Distance = fractal.Distance,
            Min = fractal.Min,
            Max = fractal.Max,
            DefInterest = fractal.DefInterest,
            FromStartInterest = fractal.FromStartInterest,
            Peak = fractal.TickPeak,
            Info = fractal.Info,
        };
    }
}
