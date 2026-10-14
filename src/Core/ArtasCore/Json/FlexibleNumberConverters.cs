using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArtasCore.Json;

/// <summary>
/// Конвертер <see cref="long"/>: принимает как целые (106), так и дробные (106.0)
/// значения, а также строки ("106"). Python сериализует числа, не различая int/float,
/// поэтому в JSONB метаданных встречаются значения вида <c>106.0</c>.
/// </summary>
public sealed class FlexibleLongConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out var l))
                {
                    return l;
                }
                return (long)Math.Round(reader.GetDouble());
            case JsonTokenType.String:
                var s = reader.GetString();
                if (long.TryParse(s, out var parsed))
                {
                    return parsed;
                }
                if (double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var d))
                {
                    return (long)Math.Round(d);
                }
                return 0;
            case JsonTokenType.Null:
                return 0;
            default:
                return 0;
        }
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}

/// <summary>
/// Конвертер <see cref="int"/>: принимает целые, дробные значения и строки.
/// </summary>
public sealed class FlexibleIntConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var i))
                {
                    return i;
                }
                return (int)Math.Round(reader.GetDouble());
            case JsonTokenType.String:
                var s = reader.GetString();
                if (int.TryParse(s, out var parsed))
                {
                    return parsed;
                }
                if (double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var d))
                {
                    return (int)Math.Round(d);
                }
                return 0;
            case JsonTokenType.Null:
                return 0;
            default:
                return 0;
        }
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}
