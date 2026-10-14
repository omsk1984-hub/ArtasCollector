namespace ArtasChart.Data;

/// <summary>
/// Подсчёт количества фракталов символа. Сегрегирован от чтения и health-check.
/// </summary>
public interface IFractalCounter
{
    /// <summary>
    /// Посчитать фракталы символа (для статус-строки).
    /// </summary>
    Task<long> CountFractalsAsync(
        string name, CancellationToken cancellationToken = default);
}
