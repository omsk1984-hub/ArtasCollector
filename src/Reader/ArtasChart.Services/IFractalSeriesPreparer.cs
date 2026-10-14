using ArtasCore.Models;

namespace ArtasChart.Services;

/// <summary>
/// Подготовка серии фракталов к отображению: присвоение порядковых номеров
/// (X-координата на графике). <c>FromStartInterest</c> берётся из БД как есть
/// и здесь НЕ пересчитывается.
/// </summary>
public interface IFractalSeriesPreparer
{
    /// <summary>
    /// Присвоить фракталам порядковые номера начиная с <paramref name="startIndex"/>
    /// (для дозагрузки real-time нумерация продолжается от последнего).
    /// </summary>
    void AssignIndices(IReadOnlyList<Fractal> fractals, int startIndex);
}
