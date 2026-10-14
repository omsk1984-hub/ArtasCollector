using System.Text.Json.Serialization;

namespace ArtasCore.Models;

/// <summary>
/// Пара рынка (цена и объем). Аналог Python <c>MPair</c> / <c>MarketPair</c>.
/// </summary>
public sealed record MarketPair
{
    /// <summary>Цена.</summary>
    [JsonPropertyName("price")]
    public double Price { get; init; }

    /// <summary>Объем.</summary>
    [JsonPropertyName("volume")]
    public int Volume { get; init; }

    /// <summary>Пустая пара (используется как безопасное значение по умолчанию).</summary>
    public static readonly MarketPair Empty = new();

    public MarketPair() { }

    public MarketPair(double price, int volume)
    {
        Price = price;
        Volume = volume;
    }

    public override string ToString() => $"{Price}:{Volume}";
}
