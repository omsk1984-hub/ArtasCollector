namespace ArtasChart.Data;

/// <summary>
/// Хранилище пользовательских настроек фильтра фракталов по символам
/// (таблица <c>public.symbol_filter_settings</c>). Сегрегировано от чтения фракталов,
/// чтобы ViewModel зависела только от нужной абстракции.
/// </summary>
public interface ISymbolFilterSettingsStore
{
    /// <summary>
    /// Загрузить сохранённые настройки фильтра для символа.
    /// Возвращает <c>null</c>, если для символа настроек ещё нет.
    /// </summary>
    /// <param name="name">Постоянное имя символа (например, "Si").</param>
    Task<FractalFilterSettings?> LoadAsync(
        string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохранить настройки фильтра для символа (upsert: одна запись на символ).
    /// </summary>
    /// <param name="name">Постоянное имя символа (например, "Si").</param>
    /// <param name="settings">Настройки фильтра.</param>
    Task SaveAsync(
        string name,
        FractalFilterSettings settings,
        CancellationToken cancellationToken = default);
}
