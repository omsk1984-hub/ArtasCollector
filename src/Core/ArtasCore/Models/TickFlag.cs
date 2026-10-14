namespace ArtasCore.Models;

/// <summary>
/// Флаги тика (аналог MQL5 TICK_FLAG_* из Python <c>tick.py</c>).
/// Строго типизированное представление битового набора; числовые значения
/// сохранены (2/4/8/16/32/64) для совместимости с JSON/БД и Python.
/// </summary>
[Flags]
public enum TickFlag
{
    /// <summary>Нет установленных флагов.</summary>
    None = 0,

    /// <summary>0000010 — тик изменил цену Bid.</summary>
    Bid = 2,

    /// <summary>0000100 — тик изменил цену Ask.</summary>
    Ask = 4,

    /// <summary>0001000 — тик изменил цену последней сделки.</summary>
    Last = 8,

    /// <summary>0010000 — тик изменил объем.</summary>
    Volume = 16,

    /// <summary>0100000 — сделка на покупку.</summary>
    Buy = 32,

    /// <summary>1000000 — сделка на продажу.</summary>
    Sell = 64,
}

/// <summary>
/// Расширения для работы с <see cref="TickFlag"/>: проверка, добавление,
/// определение направления. Заменяют ручное оперирование битами.
/// </summary>
public static class TickFlagExtensions
{
    /// <summary>Проверить наличие всех битов <paramref name="mask"/>.</summary>
    public static bool HasAll(this TickFlag flags, TickFlag mask)
        => (flags & mask) == mask;

    /// <summary>Проверить наличие хотя бы одного бита <paramref name="mask"/>.</summary>
    public static bool HasAny(this TickFlag flags, TickFlag mask)
        => (flags & mask) != 0;

    /// <summary>Добавить биты <paramref name="add"/> к набору.</summary>
    public static TickFlag With(this TickFlag flags, TickFlag add)
        => flags | add;

    /// <summary>
    /// Определить направление сделки по флагам. Аналог Python <c>IsBuy(flags)</c>.
    /// Приоритет у <see cref="TickFlag.Buy"/> (как в оригинале).
    /// </summary>
    /// <returns>true — покупка, false — продажа, null — неизвестно.</returns>
    public static bool? Direction(this TickFlag flags)
    {
        if (flags.HasAny(TickFlag.Buy))
        {
            return true;
        }
        if (flags.HasAny(TickFlag.Sell))
        {
            return false;
        }
        return null;
    }

    /// <summary>
    /// Собрать флаги тика-сделки: изменение last/объёма плюс направление сделки.
    /// </summary>
    /// <param name="side">Направление сделки (покупка/продажа).</param>
    /// <param name="changedLast">Проставлять флаг <see cref="TickFlag.Last"/>.</param>
    /// <param name="changedVolume">Проставлять флаг <see cref="TickFlag.Volume"/>.</param>
    public static TickFlag ForTrade(TradeSide side, bool changedLast = true, bool changedVolume = true)
    {
        var flags = TickFlag.None;
        if (changedLast)
        {
            flags = flags.With(TickFlag.Last);
        }
        if (changedVolume)
        {
            flags = flags.With(TickFlag.Volume);
        }
        return flags.With(side.ToFlag());
    }
}

/// <summary>
/// Направление сделки (нейтрально к типу protobuf <c>Side</c>).
/// </summary>
public enum TradeSide
{
    /// <summary>Покупка (buy).</summary>
    Buy,

    /// <summary>Продажа (sell).</summary>
    Sell,
}

/// <summary>
/// Расширения для <see cref="TradeSide"/>.
/// </summary>
public static class TradeSideExtensions
{
    /// <summary>Соответствующий флаг направления.</summary>
    public static TickFlag ToFlag(this TradeSide side)
        => side == TradeSide.Sell ? TickFlag.Sell : TickFlag.Buy;
}
