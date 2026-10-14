using System.Text.Json.Serialization;

namespace ArtasCore.Models;

/// <summary>
/// Элемент буфера рыночных пар: пара Ask/Bid + открытый интерес.
/// Аналог Python <c>MarketPairBufferItem</c>.
/// </summary>
public sealed record MarketPairBufferItem
{
    /// <summary>Пара стакана: [0] — Ask, [1] — Bid.</summary>
    [JsonPropertyName("market_pairs")]
    public IReadOnlyList<MarketPair> MarketPairs { get; init; } = DefaultMarketPairs();

    /// <summary>Открытый интерес.</summary>
    [JsonPropertyName("interest")]
    public long Interest { get; init; }

    /// <summary>Ask-пара (MarketPairs[0]).</summary>
    [JsonIgnore]
    public MarketPair MAsk => MarketPairs.Count > 0 ? MarketPairs[0] : MarketPair.Empty;

    /// <summary>Bid-пара (MarketPairs[1]).</summary>
    [JsonIgnore]
    public MarketPair MBid => MarketPairs.Count > 1 ? MarketPairs[1] : MarketPair.Empty;

    /// <summary>Сумма объемов Ask и Bid.</summary>
    [JsonIgnore]
    public double Sum => MAsk.Volume + MBid.Volume;

    /// <summary>Проверить равенство цен Ask/Bid (аналог Python <c>equal</c>).</summary>
    public bool Equal(MarketPairBufferItem other) =>
        MAsk.Price == other.MAsk.Price && MBid.Price == other.MBid.Price;

    private static List<MarketPair> DefaultMarketPairs() =>
        new()
        {
            new MarketPair(0.0, 0),
            new MarketPair(0.0, 0),
        };

    public override string ToString() => $"({MAsk}-{MBid})";
}
