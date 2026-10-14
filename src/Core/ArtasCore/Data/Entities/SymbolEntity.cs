namespace ArtasCore.Data.Entities;

/// <summary>
/// EF-сущность таблицы <c>public.symbols</c>.
/// Используется исключительно для описания схемы и миграций EF Core;
/// рантайм-доступ к данным в приложениях остаётся на Npgsql.
/// </summary>
public sealed class SymbolEntity
{
    /// <summary>Первичный ключ (symbols.id, SERIAL).</summary>
    public int Id { get; set; }

    /// <summary>Постоянное уникальное имя (ключ поиска для сервисов, например "Si").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Реальное имя контракта MT5 (например, "SiZ6").</summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>Описание.</summary>
    public string? Description { get; set; }

    /// <summary>Количество знаков после запятой.</summary>
    public int? Digits { get; set; }

    /// <summary>Минимальный шаг цены.</summary>
    public double? Point { get; set; }

    /// <summary>Дата создания записи.</summary>
    public DateTime CreatedAt { get; set; }
}
