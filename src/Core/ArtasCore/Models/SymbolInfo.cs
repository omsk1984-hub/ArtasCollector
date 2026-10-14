namespace ArtasCore.Models;

/// <summary>
/// Информация о символе из таблицы <c>public.symbols</c>.
/// Аналог Python <c>SymbolInfo</c> (namedtuple).
/// </summary>
public sealed record SymbolInfo
{
    /// <summary>Первичный ключ (symbols.id).</summary>
    public int Id { get; init; }

    /// <summary>Постоянное имя символа (ключ поиска, например, "Si").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Код контракта MT5 (например, "SiZ6").</summary>
    public string Symbol { get; init; } = string.Empty;

    /// <summary>Описание.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Количество знаков после запятой.</summary>
    public int Digits { get; init; } = 3;

    /// <summary>Минимальный шаг цены (trade_tick_size).</summary>
    public double Point { get; init; } = 0.001;

    public override string ToString() =>
        $"{Name} ({Symbol}) digits={Digits} point={Point}";
}
