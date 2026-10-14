using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArtasTools.MemReport.Models;

/// <summary>
/// Конвертер <see cref="JsonElementRaw"/>: сохраняет исходное текстовое
/// представление JSON-значения (число или строка) без потери точности.
/// </summary>
public sealed class JsonElementRawConverter : JsonConverter<JsonElementRaw>
{
    public override JsonElementRaw Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.TokenType switch
        {
            JsonTokenType.Number => reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonTokenType.String => reader.GetString() ?? "",
            _ => ""
        };
        return new JsonElementRaw { Raw = raw };
    }

    public override void Write(Utf8JsonWriter writer, JsonElementRaw value, JsonSerializerOptions options)
    {
        if (double.TryParse(value.Raw, System.Globalization.CultureInfo.InvariantCulture, out var d))
        {
            writer.WriteNumberValue(d);
        }
        else
        {
            writer.WriteStringValue(value.Raw);
        }
    }
}
