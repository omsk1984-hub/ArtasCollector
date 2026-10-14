using System.Text.Json.Serialization;
using ArtasCore.Json;

namespace ArtasCore.Models;

/// <summary>
/// Фрактал. Аналог Python <c>Fractal</c>.
/// </summary>
public sealed class Fractal
{
    /// <summary>Порядковый номер (начинается с 1, присваивается при добавлении в буфер).</summary>
    public int Index { get; set; }

    /// <summary>Расстояние (в пунктах).</summary>
    public double Distance { get; set; }

    /// <summary>Цена фрактала.</summary>
    public double Price { get; set; }

    /// <summary>Минимум.</summary>
    public double Min { get; set; }

    /// <summary>Максимум.</summary>
    public double Max { get; set; }

    /// <summary>Разность интересов с предыдущим фракталом.</summary>
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long DefInterest { get; set; }

    /// <summary>Прирост интереса от начала дня.</summary>
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long FromStartInterest { get; set; }

    /// <summary>Идентификатор (обычно time_msc пика).</summary>
    public long Id { get; set; }

    /// <summary>Направление: true — верхний, false — нижний.</summary>
    public bool IsUp { get; set; }

    /// <summary>Тик пика.</summary>
    public MyTick TickPeak { get; set; } = new();

    /// <summary>Информация о фрактале.</summary>
    public InfoF Info { get; set; } = new();

    /// <summary>Статистика фрактала.</summary>
    public StatF Stat { get; set; } = new();

    public Fractal() { }

    /// <summary>
    /// Создать фрактал по направлению и тику пика. Аналог Python <c>Fractal(is_up, pick_tick)</c>.
    /// </summary>
    public Fractal(bool isUp, MyTick pickTick)
    {
        ArgumentNullException.ThrowIfNull(pickTick);
        TickPeak = pickTick;
        Id = pickTick.TimeMsc;
        IsUp = isUp;
    }

    /// <summary>Интерес пика.</summary>
    [JsonIgnore]
    public long Interest => TickPeak.Interest;

    public override string ToString() =>
        $"{(IsUp ? "U" : "D")}Id={Id} P={Price} d={Distance} peak={TickPeak} " +
        $"I={Index} DI={DefInterest} FSI={FromStartInterest}";
}
