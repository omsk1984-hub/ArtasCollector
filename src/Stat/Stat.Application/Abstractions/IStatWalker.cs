namespace Stat.Application.Abstractions;

/// <summary>
/// Batch-обработчик статистики фракталов: читает фракталы порциями из БД,
/// прогоняет stat-курсор и записывает результат, после чего завершается.
/// Аналог Python <c>artas2026.services.stat_service.StatService</c>.
/// </summary>
public interface IStatWalker
{
    /// <summary>
    /// Запустить полный прогон расчёта статистики. Метод завершается после
    /// обработки всех доступных фракталов.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Количество сохранённых записей статистики.</returns>
    Task<int> RunAsync(CancellationToken cancellationToken = default);
}
