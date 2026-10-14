namespace ArtasCore.Data.Entities;

/// <summary>
/// EF-сущность таблицы <c>public.fractals</c> (партиционирована по <c>symbol_id</c>).
/// Партиционирование и триггер автопартиций добавляются raw SQL в миграции,
/// так как EF Core не поддерживает декларативное партиционирование PostgreSQL.
/// </summary>
public sealed class FractalEntity
{
    /// <summary>Идентификатор (обычно time_msc пика). Часть составного PK.</summary>
    public long Id { get; set; }

    /// <summary>Внешний ключ на <c>symbols.id</c>. Часть составного PK и ключ партиционирования.</summary>
    public int SymbolId { get; set; }

    /// <summary>Цена фрактала.</summary>
    public double Price { get; set; }

    /// <summary>Время формирования фрактала.</summary>
    public DateTime Time { get; set; }

    /// <summary>Дополнительные метаданные (JSONB).</summary>
    public string? Metadata { get; set; }

    /// <summary>Статистические данные фрактала (JSONB).</summary>
    public string? Stat { get; set; }

    /// <summary>Флаг soft delete.</summary>
    public bool Deleted { get; set; }

    /// <summary>Дата создания записи.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Навигация на символ.</summary>
    public SymbolEntity? Symbol { get; set; }
}
