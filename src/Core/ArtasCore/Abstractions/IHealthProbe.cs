namespace ArtasCore.Abstractions;

/// <summary>
/// Проверка доступности внешнего ресурса (БД). Сегрегирована от чтения данных.
/// </summary>
public interface IHealthProbe
{
    /// <summary>
    /// Проверить подключение к БД (SELECT 1).
    /// </summary>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}
