namespace ArtasTools.MemReport.Analysis;

/// <summary>Результат линейного анализа одной серии.</summary>
public readonly record struct TrendResult(
    double SlopePerHour,
    double R2,
    int Samples,
    double First,
    double Last,
    double Min,
    double Max)
{
    /// <summary>
    /// Наклон тренда по нижней огибающей (post-GC минимумы), МБ/ч.
    /// Растущая нижняя огибающая — ключевой признак утечки: после каждой сборки
    /// heap возвращается не к прежней базовой линии, а к более высокой.
    /// </summary>
    public double BaselineSlopePerHour { get; init; }

    /// <summary>R² фита по нижней огибающей.</summary>
    public double BaselineR2 { get; init; }

    /// <summary>Число точек, вошедших в нижнюю огибающую (post-GC минимумы).</summary>
    public int BaselineSamples { get; init; }

    /// <summary>Длительность наблюдения серии, секунды (last.T − first.T).</summary>
    public long DurationSeconds { get; init; }

    /// <summary>Метка времени первой точки, unix-секунды.</summary>
    public long FirstTimestamp { get; init; }

    /// <summary>Метка времени последней точки, unix-секунды.</summary>
    public long LastTimestamp { get; init; }

    /// <summary>Достаточно ли длинное окно, чтобы тренду можно было доверять.</summary>
    public bool IsReliable(int minSamples, long minDurationSeconds) =>
        Samples >= minSamples && DurationSeconds >= minDurationSeconds;
}

/// <summary>
/// Линейная регрессия по временному ряду (МНК). Наклон приводится к «единиц/час»,
/// что позволяет оценивать скорость роста памяти и выявлять утечку.
/// Дополнительно считается тренд по нижней огибающей (post-GC минимумы) — он
/// устойчив к «пилообразному» меандру сборок GC и не даёт ложных срабатываний
/// на коротких стартовых сериях.
/// </summary>
public static class TrendAnalyzer
{
    /// <summary>
    /// Дополнительный анализ по нижней огибающей: берём локальные минимумы серии,
    /// которые соответствуют состоянию «сразу после сборки GC». Если их линейный
    /// тренд растёт — это признак долгоживущих (Gen2) объектов / утечки.
    /// </summary>
    public static (double SlopePerHour, double R2, int Samples) AnalyzeBaseline(
        IReadOnlyList<(long T, double V)> points)
    {
        if (points.Count < 3) return (0, 0, 0);

        // Локальный минимум — точка, не превышающая обоих соседей в окне ±k.
        // k=1 достаточно для снятия «пилы» сборок при step=60s.
        const int k = 1;
        var minima = new List<(long T, double V)>();
        for (int i = 0; i < points.Count; i++)
        {
            bool isMin = true;
            for (int d = 1; d <= k; d++)
            {
                if (i - d >= 0 && points[i - d].V < points[i].V) { isMin = false; break; }
                if (i + d < points.Count && points[i + d].V < points[i].V) { isMin = false; break; }
            }
            if (isMin) minima.Add(points[i]);
        }

        // Слишком мало минимумов — тренд по ним недостоверен.
        if (minima.Count < 3) return (0, 0, minima.Count);

        var trend = Fit(minima);
        return (trend.SlopePerHour, trend.R2, minima.Count);
    }

    /// <summary>
    /// Вычислить тренд по последовательности (timeSeconds, value).
    /// </summary>
    public static TrendResult Analyze(IReadOnlyList<(long T, double V)> points)
    {
        var n = points.Count;
        if (n < 2)
        {
            double only = n == 1 ? points[0].V : 0.0;
            long t = n == 1 ? points[0].T : 0;
            return new TrendResult(0, 0, n, only, only, only, only)
            {
                FirstTimestamp = t,
                LastTimestamp = t,
                DurationSeconds = 0
            };
        }

        var fit = Fit(points);

        double min = double.MaxValue, max = double.MinValue;
        foreach (var p in points)
        {
            if (p.V < min) min = p.V;
            if (p.V > max) max = p.V;
        }

        var baseline = AnalyzeBaseline(points);

        return new TrendResult(fit.SlopePerHour, fit.R2, n, points[0].V, points[n - 1].V, min, max)
        {
            BaselineSlopePerHour = baseline.SlopePerHour,
            BaselineR2 = baseline.R2,
            BaselineSamples = baseline.Samples,
            FirstTimestamp = points[0].T,
            LastTimestamp = points[n - 1].T,
            DurationSeconds = points[n - 1].T - points[0].T
        };
    }

    /// <summary>Линейный МНК по точкам (время → значение), наклон приводится к «/час».</summary>
    private static (double SlopePerHour, double R2) Fit(IReadOnlyList<(long T, double V)> points)
    {
        var n = points.Count;
        if (n < 2) return (0.0, 0.0);

        var t0 = points[0].T;
        double meanX = 0, meanY = 0;
        for (int i = 0; i < n; i++)
        {
            meanX += (points[i].T - t0) / 3600.0;
            meanY += points[i].V;
        }
        meanX /= n;
        meanY /= n;

        double sxx = 0, sxy = 0, syy = 0;
        for (int i = 0; i < n; i++)
        {
            double dx = (points[i].T - t0) / 3600.0 - meanX;
            double dy = points[i].V - meanY;
            sxx += dx * dx;
            sxy += dx * dy;
            syy += dy * dy;
        }

        double slope = sxx != 0 ? sxy / sxx : 0.0;
        double r2 = (sxx != 0 && syy != 0) ? (sxy * sxy) / (sxx * syy) : 0.0;
        return (slope, r2);
    }
}
