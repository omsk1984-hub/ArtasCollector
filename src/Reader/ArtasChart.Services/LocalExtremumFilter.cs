using ArtasCore.Models;

namespace ArtasChart.Services;

/// <summary>
/// Реализация фильтра локального экстремума. Работает по позиции в серии
/// (серия предполагается отсортированной по Index возрастанию).
/// </summary>
public sealed class LocalExtremumFilter : ILocalExtremumFilter
{
    public IReadOnlyList<Fractal> Apply(IReadOnlyList<Fractal> series, int neighbors)
    {
        if (series is null)
        {
            return Array.Empty<Fractal>();
        }

        if (neighbors <= 0)
        {
            return series;
        }

        var result = new List<Fractal>(series.Count);

        for (var i = 0; i < series.Count; i++)
        {
            // Краевые случаи (меньше N соседей с любой стороны) — скрываем.
            if (i < neighbors || i + neighbors >= series.Count)
            {
                continue;
            }

            var f = series[i];
            var keep = f.IsUp
                ? IsLocalMax(series, i, neighbors)
                : IsLocalMin(series, i, neighbors);

            if (keep)
            {
                result.Add(f);
            }
        }

        return result;
    }

    /// <summary>UP — локальный максимум: N соседей слева и справа имеют цену ≤ цены пика.</summary>
    private static bool IsLocalMax(IReadOnlyList<Fractal> s, int i, int n)
    {
        var price = s[i].Price;
        for (var k = 1; k <= n; k++)
        {
            if (s[i - k].Price > price || s[i + k].Price > price)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>DOWN — локальный минимум: N соседей слева и справа имеют цену ≥ цены пика.</summary>
    private static bool IsLocalMin(IReadOnlyList<Fractal> s, int i, int n)
    {
        var price = s[i].Price;
        for (var k = 1; k <= n; k++)
        {
            if (s[i - k].Price < price || s[i + k].Price < price)
            {
                return false;
            }
        }

        return true;
    }
}
