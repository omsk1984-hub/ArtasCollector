using ArtasCore.Models;

namespace ArtasCore.Abstractions;

/// <summary>
/// Персистентность статистики фракталов (JSONB-поле <c>stat</c> таблицы <c>fractals</c>).
/// Аналог Python-методов <c>FractalDbService.clear_stat()</c> и
/// <c>FractalDbService.update_fractal_stat()</c>.
/// </summary>
public interface IStatRepository
{
    /// <summary>
    /// Очистить статистику по всем фракталам символа
    /// (<c>UPDATE fractals SET stat = null WHERE symbol_id = ...</c>).
    /// </summary>
    /// <param name="symbolId">Идентификатор символа из <c>public.symbols.id</c>.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task ClearStatAsync(int symbolId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Записать статистику для одного фрактала
    /// (<c>UPDATE fractals SET stat = %s WHERE id = %s AND symbol_id = %s</c>).
    /// </summary>
    /// <param name="symbolId">Идентификатор символа из <c>public.symbols.id</c>.</param>
    /// <param name="fractalId">Идентификатор фрактала.</param>
    /// <param name="stat">Статистика фрактала.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>true, если строка обновлена.</returns>
    Task<bool> UpdateStatAsync(
        int symbolId,
        long fractalId,
        StatF stat,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Записать статистику для пачки фракталов одним запросом (батчинг через unnest).
    /// </summary>
    /// <param name="symbolId">Идентификатор символа из <c>public.symbols.id</c>.</param>
    /// <param name="records">Пары (fractalId, stat).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Количество обновлённых строк.</returns>
    Task<int> UpdateStatBatchAsync(
        int symbolId,
        IReadOnlyList<(long FractalId, StatF Stat)> records,
        CancellationToken cancellationToken = default);
}
