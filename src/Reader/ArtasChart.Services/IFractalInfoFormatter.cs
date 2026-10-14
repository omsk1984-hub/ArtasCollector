using ArtasCore.Models;

namespace ArtasChart.Services;

/// <summary>
/// Формирование текста info-панели для выбранного фрактала (чистая функция,
/// легко тестируется без UI).
/// </summary>
public interface IFractalInfoFormatter
{
    /// <summary>
    /// Сформировать многострочный текст описания фрактала (или подсказку при null).
    /// </summary>
    string Format(Fractal? fractal);

    /// <summary>
    /// Сформировать одну компактную строку-сводку фрактала для копирования
    /// в буфер обмена (ключевые поля: номер, направление, время, цена, стакан, результат).
    /// Возвращает пустую строку, если фрактал не задан.
    /// </summary>
    string FormatClipboard(Fractal? fractal);
}