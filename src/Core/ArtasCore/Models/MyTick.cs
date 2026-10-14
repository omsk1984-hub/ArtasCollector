using System.Text.Json.Serialization;
using ArtasCore.Json;

namespace ArtasCore.Models;

/// <summary>
/// Тик модели. Аналог Python <c>MyTick</c> (наследник <c>MarketPairBufferItem</c>).
/// Иммутабельная запись (record) — соответствует frozen dataclass.
/// </summary>
public sealed record MyTick
{
    /// <summary>Стакан (Ask/Bid) на момент тика. В Python — <c>market_pairs</c>.</summary>
    [JsonPropertyName("market_pairs")]
    public IReadOnlyList<MarketPair> MarketPairs { get; init; } = DefaultMarketPairs();

    /// <summary>Открытый интерес.</summary>
    [JsonPropertyName("interest")]
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long Interest { get; init; }

    /// <summary>Объем тика.</summary>
    [JsonPropertyName("Volume")]
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long Volume { get; init; }

    /// <summary>Цена Ask.</summary>
    [JsonPropertyName("Ask")]
    public double Ask { get; init; }

    /// <summary>Цена Bid.</summary>
    [JsonPropertyName("Bid")]
    public double Bid { get; init; }

    /// <summary>Последняя цена сделки.</summary>
    [JsonPropertyName("Last")]
    public double Last { get; init; }

    /// <summary>Флаги тика (TICK_FLAG_*). Числовое представление для JSON/БД-совместимости.</summary>
    [JsonPropertyName("flags")]
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int Flags { get; init; }

    /// <summary>Типизированное представление флагов (см. <see cref="TickFlag"/>).</summary>
    [JsonIgnore]
    public TickFlag FlagsEnum => (TickFlag)Flags;

    /// <summary>Время в миллисекундах (UTC).</summary>
    [JsonPropertyName("time_msc")]
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long TimeMsc { get; init; }

    /// <summary>Ask-пара стакана (MarketPairs[0]).</summary>
    [JsonIgnore]
    public MarketPair MAsk => MarketPairs.Count > 0 ? MarketPairs[0] : MarketPair.Empty;

    /// <summary>Bid-пара стакана (MarketPairs[1]).</summary>
    [JsonIgnore]
    public MarketPair MBid => MarketPairs.Count > 1 ? MarketPairs[1] : MarketPair.Empty;

    /// <summary>Сумма объемов Ask и Bid.</summary>
    [JsonIgnore]
    public double Sum => MAsk.Volume + MBid.Volume;

    /// <summary>Время тика в UTC (исходное биржевое время).</summary>
    [JsonIgnore]
    public DateTime TimeUtc => DateTimeOffset.FromUnixTimeMilliseconds(TimeMsc).UtcDateTime;

    /// <summary>
    /// Время тика в московском времени (UTC+3) — локальное время для отображения.
    /// Используется как основное <see cref="TimeMsk"/> во всех UI-представлениях.
    /// </summary>
    [JsonIgnore]
    public DateTime TimeMsk => TimeUtc.Add(TimeSpan.FromHours(3));

    /// <summary>Направление сделки: true — покупка, false — продажа, null — неизвестно.</summary>
    [JsonIgnore]
    public bool? IsBuy => FlagsEnum.Direction();

    /// <summary>Цена покупки: <c>Last</c>, если тик — покупка, иначе 0. Аналог Python <c>Buy</c>.</summary>
    [JsonIgnore]
    public double Buy => IsBuy == true ? Last : 0.0;

    /// <summary>Цена продажи: <c>Last</c>, если тик — продажа, иначе 0. Аналог Python <c>Sell</c>.</summary>
    [JsonIgnore]
    public double Sell => IsBuy == false ? Last : 0.0;

    private static List<MarketPair> DefaultMarketPairs() =>
        new()
        {
            new MarketPair(0.0, 0),
            new MarketPair(0.0, 0),
        };

    public override string ToString() =>
        $"MT({(IsBuy == true ? 'B' : IsBuy == false ? 'S' : '?')} " +
        $"{TimeMsk:yyyyMMdd_HH:mm:ss} {Flags} L{Last} V{Volume} A{Ask} B{Bid} I{Interest})";
}
