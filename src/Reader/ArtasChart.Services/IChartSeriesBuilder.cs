using ArtasCore.Models;

namespace ArtasChart.Services;

/// <summary>
/// Подготовка данных к отображению на графике: фильтрация серии фракталов
/// и разбиение точек на серии «take»/«other», а также данные гистограммы и
/// границ дней. <c>FromStartInterest</c> берётся из БД как есть и здесь не
/// пересчитывается. Отделяет презентационную логику от View (тонкий
/// code-behind) и от ViewModel.
/// </summary>
public interface IChartSeriesBuilder
{
    /// <summary>
    /// Полностью подготовить серию к отображению: применить фильтр локального
    /// экстремума (если включён) и объёмные фильтры, вернуть готовую модель
    /// представления.
    /// </summary>
    /// <param name="allFractals">Полная серия (по возрастанию Index).</param>
    /// <param name="options">Параметры фильтрации (пороги объёмов).</param>
    /// <param name="extremumNeighbors">Число соседей N для фильтра экстремума (0 — выкл).</param>
    /// <param name="point">
    /// Минимальный шаг цены символа (<c>symbols.point</c>) — пробрасывается в
    /// <see cref="ChartSeries.Point"/> для выражения разности цен в пунктах.
    /// 0 означает «неизвестно».
    /// </param>
    ChartSeries Build(
        IReadOnlyList<Fractal> allFractals,
        FractalFilterOptions options,
        int extremumNeighbors,
        double point);
}
