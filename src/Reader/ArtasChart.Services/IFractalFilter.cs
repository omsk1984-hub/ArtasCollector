using ArtasCore.Models;

namespace ArtasChart.Services;

/// <summary>
/// Фильтрация фракталов по составным критериям (открыта для расширения:
/// можно добавить новые фильтры, не меняя контрол отображения).
/// </summary>
public interface IFractalFilter
{
    /// <summary>
    /// Применить критерии фильтрации к исходному набору фракталов.
    /// </summary>
    IReadOnlyList<Fractal> Filter(IReadOnlyList<Fractal> source, FractalFilterOptions options);

    /// <summary>
    /// Проверить, проходит ли конкретный фрактал объёмный фильтр (по стакану Ask/Bid
    /// и Info.Volume). Нужен, в частности, для фильтрации красных точек (невзятий).
    /// </summary>
    bool PassesVolume(Fractal fractal, FractalFilterOptions options);

    /// <summary>
    /// Проверить прохождение фильтра <c>Min Volume</c> (только <c>Info.Volume</c>).
    /// Используется для красных точек (невзятий).
    /// </summary>
    bool PassesMinVolume(Fractal fractal, FractalFilterOptions options);

    /// <summary>
    /// Проверить прохождение фильтра <c>Min Pick Volume</c> (только
    /// <c>Info.PickVolume</c>). Используется для красных точек (невзятий).
    /// </summary>
    bool PassesMinPickVolume(Fractal fractal, FractalFilterOptions options);

    /// <summary>
    /// Проверить прохождение фильтра по стакану <c>Min Markets Ask</c> (Markets[0])
    /// или <c>Min Markets Bid</c> (Markets[1]). Используется для зелёных точек.
    /// </summary>
    bool PassesMinMarkets(Fractal fractal, FractalFilterOptions options);
}
