using ArtasCore.Models;

namespace ArtasChart.Services;

/// <summary>
/// Реализация подготовки серии фракталов (без изменений в исходных данных UI-слоя).
/// <c>FromStartInterest</c> не пересчитывается — используется значение из БД.
/// </summary>
public sealed class FractalSeriesPreparer : IFractalSeriesPreparer
{
    public void AssignIndices(IReadOnlyList<Fractal> fractals, int startIndex)
    {
        for (var i = 0; i < fractals.Count; i++)
        {
            fractals[i].Index = startIndex + i + 1;
        }
    }
}
