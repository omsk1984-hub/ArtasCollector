using System;
using System.Collections.Generic;
using ArtasCore.Models;

namespace ArtasChart.Services;

/// <summary>
/// Готовая к отображению модель серии фракталов (результат <see cref="IChartSeriesBuilder"/>).
/// Не содержит UI-зависимостей — только подготовленные данные.
/// </summary>
/// <param name="AllFractals">Полная серия (для счётчиков и карты Index→Time).</param>
/// <param name="visibleFractals">
/// База отрисовки после экстремум-фильтра: по ней строятся линия, гистограмма,
/// границы дней и оси (объёмный фильтр её не затрагивает). Красные точки
/// (невзятия) дополнительно фильтруются по объёму стакана на уровне массивов
/// <see cref="OtherXs"/>/<see cref="OtherYs"/>.
/// </param>
public sealed class ChartSeries
{
    public ChartSeries(IReadOnlyList<Fractal> allFractals, IReadOnlyList<Fractal> visibleFractals)
    {
        AllFractals = allFractals;
        VisibleFractals = visibleFractals;
    }

    /// <summary>Полная серия фракталов (включая скрытые экстремум-фильтром).</summary>
    public IReadOnlyList<Fractal> AllFractals { get; }

    /// <summary>
    /// База отрисовки после экстремум-фильтра (без объёмного фильтра):
    /// линия, синие точки, гистограмма, границы дней и оси.
    /// </summary>
    public IReadOnlyList<Fractal> VisibleFractals { get; }

    /// <summary>Количество фракталов в базе отрисовки.</summary>
    public int VisibleCount => VisibleFractals.Count;

    /// <summary>Общее количество фракталов.</summary>
    public int TotalCount => AllFractals.Count;

    /// <summary>X-координаты (Index) базовой серии (линия + все вершины).</summary>
    public double[] Xs { get; init; } = Array.Empty<double>();

    /// <summary>Y-координаты (Price) базовой серии.</summary>
    public double[] Prices { get; init; } = Array.Empty<double>();

    /// <summary>X точек-«взятий» (Stat.IsTake == true) — синие, независимо от объёма.</summary>
    public double[] TakeXs { get; init; } = Array.Empty<double>();

    /// <summary>Y точек-«взятий».</summary>
    public double[] TakeYs { get; init; } = Array.Empty<double>();

    /// <summary>
    /// X красных точек (невзятия), прошедших фильтр <c>Min Volume</c>
    /// (независимо от стакана Ask/Bid).
    /// </summary>
    public double[] OtherXs { get; init; } = Array.Empty<double>();

    /// <summary>Y красных точек (невзятия), прошедших фильтр <c>Min Volume</c>.</summary>
    public double[] OtherYs { get; init; } = Array.Empty<double>();

    /// <summary>
    /// X зелёных точек (невзятия), прошедших фильтр по стакану
    /// <c>Min Markets Ask</c> (Markets[0]) или <c>Min Markets Bid</c> (Markets[1]).
    /// </summary>
    public double[] GreenXs { get; init; } = Array.Empty<double>();

    /// <summary>Y зелёных точек (невзятия), прошедших фильтр по стакану Ask/Bid.</summary>
    public double[] GreenYs { get; init; } = Array.Empty<double>();

    /// <summary>
    /// Минимальный шаг цены символа (<c>symbols.point</c>). Используется для
    /// выражения разности цен в пунктах (ΔY / point). Значение 0 трактуется как
    /// «неизвестно» — в этом случае деление не выполняется.
    /// </summary>
    public double Point { get; init; }

    /// <summary>Карта Index → время пика в МСК (для подписей оси X).</summary>
    public IReadOnlyDictionary<int, DateTime> IndexToTime { get; init; } =
        new Dictionary<int, DateTime>();

    /// <summary>X-координаты границ дней (Index нового дня − 0.5).</summary>
    public double[] DayBoundariesX { get; init; } = Array.Empty<double>();
}
