using ArtasCore.Models;

namespace ArtasCore.Abstractions;

/// <summary>
/// Персистентность фракталов в PostgreSQL. Аналог Python
/// <c>FractalWriterService.save_fractal()</c>.
/// </summary>
public interface IFractalRepository
{
    /// <summary>
    /// Сохранить один фрактал. Идемпотентно
    /// (<c>ON CONFLICT (id, symbol_id) DO NOTHING</c>).
    /// </summary>
    /// <param name="symbolId">Идентификатор символа из <c>public.symbols.id</c>.</param>
    /// <param name="fractal">Фрактал.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>true, если запись добавлена; false — если конфликт (уже существует).</returns>
    Task<bool> SaveAsync(
        int symbolId,
        Fractal fractal,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохранить пачку фракталов одним запросом с батчингом.
    /// </summary>
    Task<int> SaveBatchAsync(
        IReadOnlyList<(int SymbolId, Fractal Fractal)> records,
        CancellationToken cancellationToken = default);

    /// <summary>Получить количество фракталов символа за текущий день (UTC).</summary>
    Task<int> CountTodayAsync(int symbolId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить открытый интерес самого раннего боевого фрактала текущего дня (UTC).
    /// Используется Writer при старте/перезапуске, чтобы рассчитать
    /// <c>FromStartInterest</c> относительно начала дня, а не относительно
    /// первого полученного после рестарта фрактала. Записи-симуляции
    /// (<c>stat = '"Fake"'</c>) игнорируются.
    /// </summary>
    /// <returns>
    /// Значение <c>metadata.interest</c> раннего фрактала дня либо <c>null</c>,
    /// если за текущий день боевых фракталов нет.
    /// </returns>
    Task<long?> GetDayStartInterestAsync(int symbolId, CancellationToken cancellationToken = default);
}
