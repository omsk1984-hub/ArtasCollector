using ArtasCore.Models;

namespace ArtasChart.Services;

/// <summary>
/// Фильтр отображения фракталов по локальному экстремуму.
/// UP показывается, если слева и справа N соседей имеют цену &le; его цены
/// (локальный максимум). DOWN — если слева и справа N соседей имеют цену
/// &ge; его цены (локальный минимум). Соседи — по порядку в серии (Index).
/// Краевые (меньше N соседей) скрываются.
/// </summary>
public interface ILocalExtremumFilter
{
    /// <summary>
    /// Отфильтровать серию, оставив только локальные экстремумы.
    /// </summary>
    /// <param name="series">Полная упорядоченная серия фракталов (по возрастанию Index).</param>
    /// <param name="neighbors">Число соседей N с каждой стороны. 0 — фильтр выключен.</param>
    IReadOnlyList<Fractal> Apply(IReadOnlyList<Fractal> series, int neighbors);
}
