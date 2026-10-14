using System;
using System.Collections.Generic;
using System.Linq;
using ArtasCore.Models;

namespace ArtasChart.Services;

/// <summary>
/// Реализация подготовки серии к отображению. Координирует фильтры
/// (<see cref="ILocalExtremumFilter"/>, <see cref="IFractalFilter"/>) и
/// <see cref="IFractalSeriesPreparer"/>, собирая готовую модель <see cref="ChartSeries"/>.
/// Чистая (без UI) логика — легко тестируется.
/// </summary>
public sealed class ChartSeriesBuilder : IChartSeriesBuilder
{
    private readonly IFractalFilter _filter;
    private readonly ILocalExtremumFilter _extremumFilter;
    private readonly IFractalSeriesPreparer _seriesPreparer;

    public ChartSeriesBuilder(
        IFractalFilter filter,
        ILocalExtremumFilter extremumFilter,
        IFractalSeriesPreparer seriesPreparer)
    {
        _filter = filter;
        _extremumFilter = extremumFilter;
        _seriesPreparer = seriesPreparer;
    }

    /// <inheritdoc />
    public ChartSeries Build(
        IReadOnlyList<Fractal> allFractals,
        FractalFilterOptions options,
        int extremumNeighbors,
        double point)
    {
        // FromStartInterest берётся из БД как есть (загружается в FractalRepository)
        // и здесь НЕ пересчитывается.

        // 1) Фильтр локального экстремума — по полной серии (соседство по Index).
        // Результат — база для линии, синих точек (взятий), гистограммы и осей;
        // объёмный фильтр на него НЕ влияет.
        var baseline = extremumNeighbors > 0
            ? _extremumFilter.Apply(allFractals, extremumNeighbors)
            : allFractals;

        // 2) Объёмные фильтры применяются ТОЛЬКО к точкам-невзятиям:
        //  • красные точки — прошли фильтр Min Volume (Info.Volume)
        //    ИЛИ Min Pick Volume (Info.PickVolume);
        //  • зелёные точки — прошли фильтр Min Markets Ask/Bid (Markets[0..1]).
        // Синие точки (взятия) от объёма не зависят. Точка, прошедшая оба
        // фильтра, отображается и красным, и зелёным цветом.
        return Compose(allFractals, baseline, options, point);
    }

    /// <summary>
    /// Собрать модель серии.
    /// </summary>
    /// <param name="allFractals">Полная серия (для счётчиков и карты Index→Time).</param>
    /// <param name="baseline">
    /// База отрисовки (после экстремум-фильтра): линия, синие точки (взятия),
    /// гистограмма и границы дней. Объёмным фильтром не затрагивается.
    /// </param>
    /// <param name="options">Пороги фильтрации (Min Volume и Min Markets Ask/Bid).</param>
    /// <param name="point">
    /// Минимальный шаг цены символа (<c>symbols.point</c>) для <see cref="ChartSeries.Point"/>.
    /// </param>
    private ChartSeries Compose(
        IReadOnlyList<Fractal> allFractals,
        IReadOnlyList<Fractal> baseline,
        FractalFilterOptions options,
        double point)
    {
        var xs = baseline.Select(f => (double)f.Index).ToArray();
        var prices = baseline.Select(f => f.Price).ToArray();

        var takeXs = new List<double>();
        var takeYs = new List<double>();
        var otherXs = new List<double>();
        var otherYs = new List<double>();
        var greenXs = new List<double>();
        var greenYs = new List<double>();
        for (var i = 0; i < baseline.Count; i++)
        {
            var f = baseline[i];

            if (f.Stat.IsTake)
            {
                // Синие точки (взятия) — по базовой серии, независимо от объёма.
                takeXs.Add(xs[i]);
                takeYs.Add(prices[i]);
                continue;
            }

            // Красные точки (невзятия) — прошли фильтр Min Volume (Info.Volume)
            // ИЛИ Min Pick Volume (Info.PickVolume).
            if (_filter.PassesMinVolume(f, options) ||
                _filter.PassesMinPickVolume(f, options))
            {
                otherXs.Add(xs[i]);
                otherYs.Add(prices[i]);
            }

            // Зелёные точки (невзятия) — прошли фильтр Min Markets Ask/Bid.
            if (_filter.PassesMinMarkets(f, options))
            {
                greenXs.Add(xs[i]);
                greenYs.Add(prices[i]);
            }
        }

        // Гистограмма и границы дней строятся по полной базовой серии (baseline)
        // без объёмного фильтра: фильтрация объёма влияет ТОЛЬКО на красные точки,
        // а не на бары гистограммы.
        // Время пика в МСК (TickPeak.TimeMsk — московское время).
        var indexToTime = new Dictionary<int, DateTime>(allFractals.Count);
        foreach (var f in allFractals)
        {
            indexToTime[f.Index] = f.TickPeak.TimeMsk;
        }

        return new ChartSeries(allFractals, baseline)
        {
            Point = point,
            Xs = xs,
            Prices = prices,
            TakeXs = takeXs.ToArray(),
            TakeYs = takeYs.ToArray(),
            OtherXs = otherXs.ToArray(),
            OtherYs = otherYs.ToArray(),
            GreenXs = greenXs.ToArray(),
            GreenYs = greenYs.ToArray(),
            IndexToTime = indexToTime,
            DayBoundariesX = ComputeDayBoundaries(baseline),
        };
    }

    /// <summary>
    /// X-координаты границ дней: позиция между последним фракталом предыдущего
    /// дня и первым фракталом нового дня (Index нового дня − 0.5).
    /// </summary>
    private static double[] ComputeDayBoundaries(IReadOnlyList<Fractal> visible)
    {
        if (visible.Count == 0)
        {
            return Array.Empty<double>();
        }

        var boundaries = new List<double>();
        DateTime? currentDay = null;
        for (var i = 0; i < visible.Count; i++)
        {
            var day = visible[i].TickPeak.TimeMsk.Date;
            if (currentDay is null)
            {
                currentDay = day;
                continue;
            }

            if (day != currentDay)
            {
                currentDay = day;
                boundaries.Add(visible[i].Index - 0.5);
            }
        }

        return boundaries.ToArray();
    }
}
