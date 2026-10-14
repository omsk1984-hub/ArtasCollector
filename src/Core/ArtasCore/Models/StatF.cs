using System.Text.Json.Serialization;
using ArtasCore.Json;

namespace ArtasCore.Models;

/// <summary>
/// Статистика фрактала. Аналог Python <c>StatF</c> (обычный dataclass).
/// Поля совпадают с JSONB-полем <c>stat</c> в БД.
/// </summary>
public sealed record StatF
{
    [JsonPropertyName("price_open")] public double PriceOpen { get; init; }
    [JsonPropertyName("price_stop")] public double PriceStop { get; init; }
    [JsonPropertyName("price_take")] public double PriceTake { get; init; }

    [JsonPropertyName("result_stop")] public double ResultStop { get; init; }
    [JsonPropertyName("result_take")] public double ResultTake { get; init; }

    [JsonPropertyName("index_open")]
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int IndexOpen { get; init; }

    [JsonPropertyName("index_stop")]
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int IndexStop { get; init; }

    [JsonPropertyName("index_take")]
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int IndexTake { get; init; }

    [JsonPropertyName("index_total")]
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int IndexTotal { get; init; }

    /// <summary>
    /// Направление сделки: true — покупка (лонг), false — продажа (шорт).
    /// Python-поле <c>has_buy</c> со значением по умолчанию <c>true</c>.
    /// </summary>
    [JsonPropertyName("has_buy")]
    public bool HasBuy { get; init; } = true;

    /// <summary>Сделка открыта (был установлен <c>index_open</c>). Аналог Python <c>IsOpen</c>.</summary>
    [JsonIgnore] public bool IsOpen => IndexOpen > 0;

    /// <summary>Сделка закрыта по стопу. Аналог Python <c>IsStop</c>.</summary>
    [JsonIgnore] public bool IsStop => IndexStop > 0;

    /// <summary>Сделка закрыта по тейку. Аналог Python <c>IsTake</c>.</summary>
    [JsonIgnore] public bool IsTake => IndexTake > 0;
}
