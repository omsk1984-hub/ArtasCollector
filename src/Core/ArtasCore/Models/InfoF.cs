using System.Text.Json.Serialization;
using ArtasCore.Json;

namespace ArtasCore.Models;

/// <summary>
/// Информация о фрактале. Аналог Python <c>InfoF</c> (frozen dataclass).
/// Числовые поля используют гибкий конвертер: Python пишет часть значений
/// дробными (например, <c>Volume: 161.0</c>), что не парсится в int по умолчанию.
/// </summary>
public sealed record InfoF
{
    [JsonPropertyName("PickVolume")]
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long PickVolume { get; init; }

    [JsonPropertyName("PickCount")]
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long PickCount { get; init; }

    [JsonPropertyName("GlassPickCount")]
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long GlassPickCount { get; init; }

    [JsonPropertyName("GlassPickTotal")]
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long GlassPickTotal { get; init; }

    [JsonPropertyName("Ask")] public double Ask { get; init; }
    [JsonPropertyName("Bid")] public double Bid { get; init; }

    [JsonPropertyName("RACount")]
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long RACount { get; init; }

    [JsonPropertyName("RAVolume")]
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long RAVolume { get; init; }

    [JsonPropertyName("RAVolumeBig")]
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long RAVolumeBig { get; init; }

    [JsonPropertyName("SpreadTotal")]
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long SpreadTotal { get; init; }

    [JsonPropertyName("SpreadPeakCount")]
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long SpreadPeakCount { get; init; }

    [JsonPropertyName("Volume")]
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long Volume { get; init; }

    [JsonPropertyName("VolumeUp")]
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long VolumeUp { get; init; }

    [JsonPropertyName("VolumeDown")]
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long VolumeDown { get; init; }

    [JsonPropertyName("TotalTicks")]
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long TotalTicks { get; init; }

    [JsonPropertyName("AboveCount")]
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long AboveCount { get; init; }

    [JsonPropertyName("BetweenCount")]
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long BetweenCount { get; init; }

    [JsonPropertyName("BelowCount")]
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long BelowCount { get; init; }

    /// <summary>Стакан (6 элементов). В Python — <c>Markets</c>.</summary>
    [JsonPropertyName("Markets")]
    public IReadOnlyList<MarketPair> Markets { get; init; } = DefaultMarkets();

    // Явные именованные позиции стакана (замена «магическим» индексам 0..5).
    // 0 — максимальный стакан на пике (Ask), 1 — максимальный стакан на пике (Bid),
    // 2 — максимальный стакан за фрактал (Ask), 3 — максимальный стакан за фрактал (Bid),
    // 4 — стакан на последнем тике (Ask), 5 — стакан на последнем тике (Bid).

    /// <summary>Стакан последнего тика пика, Ask (Markets[0]).</summary>
    [JsonIgnore]
    public MarketPair MarketAsk => GetMarket(0);

    /// <summary>Стакан последнего тика пика, Bid (Markets[1]).</summary>
    [JsonIgnore]
    public MarketPair MarketBid => GetMarket(1);

    /// <summary>Безопасно получить элемент стакана по индексу (пустая пара при выходе за границы).</summary>
    public MarketPair GetMarket(int index) =>
        index >= 0 && index < Markets.Count ? Markets[index] : MarketPair.Empty;

    private static List<MarketPair> DefaultMarkets() =>
        Enumerable.Range(0, 6).Select(_ => new MarketPair()).ToList();

    /// <summary>
    /// Создать <see cref="InfoF"/> из накопителя <see cref="InfoMeta"/>.
    /// Аналог Python <c>create_Info(meta)</c>.
    /// </summary>
    public static InfoF Create(InfoMeta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);

        var pairs = meta.Markets
            .Select(m => new MarketPair(m.Price, m.Volume))
            .ToList();

        if (pairs.Count < 4)
        {
            throw new InvalidOperationException(
                $"meta.Markets должен содержать 4 или 6 элементов для InfoF, получено {pairs.Count}");
        }

        return new InfoF
        {
            PickVolume = meta.PickVolume,
            PickCount = meta.PickCount,
            GlassPickCount = meta.GlassPickCount,
            GlassPickTotal = meta.GlassPickTotal,
            Ask = meta.Ask,
            Bid = meta.Bid,
            RACount = meta.RACount,
            RAVolume = meta.RAVolume,
            RAVolumeBig = meta.RAVolumeBig,
            SpreadTotal = meta.SpreadTotal,
            SpreadPeakCount = meta.SpreadPeakCount,
            Volume = meta.Volume,
            VolumeUp = meta.VolumeUp,
            VolumeDown = meta.VolumeDown,
            TotalTicks = meta.TotalTicks,
            AboveCount = meta.AboveCount,
            BetweenCount = meta.BetweenCount,
            BelowCount = meta.BelowCount,
            Markets = pairs,
        };
    }
}
