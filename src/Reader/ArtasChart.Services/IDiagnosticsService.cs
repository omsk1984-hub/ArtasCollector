using ArtasChart.Data;

namespace ArtasChart.Services;

/// <summary>
/// CLI-диагностика подключения к БД и чтения фракталов (извлечена из точки входа).
/// </summary>
public interface IDiagnosticsService
{
    /// <summary>
    /// Проверить подключение к БД и вывести статистику по символу.
    /// </summary>
    Task CheckDbAsync(string symbol);
}