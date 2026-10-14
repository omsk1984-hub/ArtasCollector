namespace ArtasCore.Data.Entities;

/// <summary>
/// EF-сущность таблицы <c>public.symbol_filter_settings</c>.
/// Хранит пользовательские настройки фильтра фракталов (Read/Writer chart)
/// по каждому символу. Все параметры фильтра — в одном JSONB-поле <see cref="Settings"/>.
/// Используется исключительно для описания схемы и миграций EF Core;
/// рантайм-доступ к данным в приложениях остаётся на Npgsql.
/// </summary>
public sealed class SymbolFilterSettingsEntity
{
    /// <summary>Первичный ключ (symbol_filter_settings.id, SERIAL).</summary>
    public int Id { get; set; }

    /// <summary>
    /// Внешний ключ на <c>symbols.id</c>. Уникален: на символ — одна запись настроек.
    /// </summary>
    public int SymbolId { get; set; }

    /// <summary>
    /// Все параметры фильтра в формате JSONB
    /// (minMarket0Volume, minMarket1Volume, minVolume, extremumNeighbors, showRedPoints).
    /// </summary>
    public string Settings { get; set; } = "{}";

    /// <summary>Дата создания записи.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Дата последнего сохранения настроек.</summary>
    public DateTime UpdatedAt { get; set; }
}
