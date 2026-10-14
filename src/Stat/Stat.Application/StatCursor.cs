using ArtasCore.Models;

namespace Stat.Application;

/// <summary>
/// Курсор расчёта статистики фрактала. Прямой перенос Python
/// <c>artas2026.core.stat_cursor.StatCursor</c>.
/// </summary>
/// <remarks>
/// Логика идентична Python-эталону:
/// <list type="bullet">
/// <item><description>цена открытия = цена + <c>open_point * point * deal_sign</c>;</description></item>
/// <item><description>стоп/тейк — от цены открытия с инвертированием знака для продажи;</description></item>
/// <item><description>все три цены округляются <c>round(x, digits)</c> (банковское округление).</description></item>
/// </list>
/// </remarks>
public sealed class StatCursor
{
    /// <summary>Символ (постоянное имя, например "CNYRUBF").</summary>
    public string Symbol { get; }

    /// <summary>Минимальный шаг цены.</summary>
    public double Point { get; set; }

    /// <summary>Количество знаков после запятой для округления цен.</summary>
    public int Digits { get; set; }

    /// <summary>Смещение открытия в пунктах (в Python — <c>open_point = 1</c>).</summary>
    public int OpenPoint { get; } = 1;

    /// <summary>Размер стопа в пунктах.</summary>
    public int StopPoint { get; }

    /// <summary>Размер тейка в пунктах.</summary>
    public int TakePoint { get; }

    /// <summary>
    /// Создать курсор.
    /// </summary>
    /// <param name="symbol">Постоянное имя символа.</param>
    /// <param name="digits">Знаков после запятой (по умолчанию 3).</param>
    /// <param name="point">Шаг цены (по умолчанию 0.001).</param>
    /// <param name="stopPoint">Размер стопа в пунктах (по умолчанию 3).</param>
    /// <param name="takePoint">Размер тейка в пунктах (по умолчанию 12, как в Python-классе).</param>
    public StatCursor(
        string symbol,
        int digits = 3,
        double point = 0.001,
        int stopPoint = 3,
        int takePoint = 12)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        Symbol = symbol;
        Digits = digits;
        Point = point;
        StopPoint = stopPoint;
        TakePoint = takePoint;
    }

    /// <summary>
    /// Инициализировать статистику для нового фрактала. Аналог Python <c>StatCursor.init()</c>.
    /// </summary>
    /// <param name="pickPrice">Цена пика фрактала.</param>
    /// <param name="hasBuy">true — покупка (лонг), false — продажа (шорт).</param>
    /// <returns>Новая статистика с рассчитанными ценами open/stop/take.</returns>
    public StatF Init(double pickPrice, bool hasBuy)
    {
        var dealSign = hasBuy ? 1 : -1;

        var priceOpen = pickPrice + OpenPoint * Point * dealSign;
        var priceStop = priceOpen - StopPoint * Point * dealSign;
        var priceTake = priceOpen + TakePoint * Point * dealSign;

        return new StatF
        {
            HasBuy = hasBuy,
            PriceOpen = RoundTo(priceOpen, Digits),
            PriceStop = RoundTo(priceStop, Digits),
            PriceTake = RoundTo(priceTake, Digits),
        };
    }

    /// <summary>
    /// Проверить состояние сделки на новом фрактале. Аналог Python <c>StatCursor.check()</c>.
    /// </summary>
    /// <param name="stat">Статистика сделки (изменяется по месту — возвращаемому значению присваивается новое состояние).</param>
    /// <param name="fractal">Проверяемый фрактал.</param>
    /// <param name="index">Индекс фрактала в последовательности (начиная с 1).</param>
    /// <returns>Модифицированная статистика и признак закрытия сделки (стоп/тейк).</returns>
    /// <remarks>
    /// Из-за иммутабельности <see cref="StatF"/> (record с init-свойствами) метод возвращает
    /// новую запись вместо мутации входной. Возвращаемый кортеж: (обновлённый StatF, закрыта ли сделка).
    /// </remarks>
    public (StatF Stat, bool IsStopped) Check(StatF stat, Fractal fractal, int index)
    {
        ArgumentNullException.ThrowIfNull(stat);
        ArgumentNullException.ThrowIfNull(fractal);

        var isOpen = stat.IsOpen;
        var indexOpen = stat.IndexOpen;

        if (stat.HasBuy)
        {
            if (!isOpen && fractal.Price < stat.PriceOpen)
            {
                indexOpen = index;
            }

            var nowOpen = indexOpen > 0;
            if (nowOpen)
            {
                if (fractal.Price <= stat.PriceStop)
                {
                    return (stat with
                    {
                        IndexTotal = index,
                        IndexOpen = indexOpen,
                        IndexStop = index,
                        ResultStop = StopPoint + 1,
                    }, true);
                }

                if (fractal.Price >= stat.PriceTake)
                {
                    return (stat with
                    {
                        IndexTotal = index,
                        IndexOpen = indexOpen,
                        IndexTake = index,
                        ResultTake = TakePoint,
                    }, true);
                }

                var takePoint = (fractal.Price - stat.PriceOpen) / Point;
                return (stat with
                {
                    IndexTotal = index,
                    IndexOpen = indexOpen,
                    ResultTake = (int)takePoint,
                }, false);
            }

            return (stat with { IndexTotal = index, IndexOpen = indexOpen }, false);
        }
        else
        {
            if (!isOpen && fractal.Price > stat.PriceOpen)
            {
                indexOpen = index;
            }

            var nowOpen = indexOpen > 0;
            if (nowOpen)
            {
                if (fractal.Price >= stat.PriceStop)
                {
                    return (stat with
                    {
                        IndexTotal = index,
                        IndexOpen = indexOpen,
                        IndexStop = index,
                        ResultStop = StopPoint + 1,
                    }, true);
                }

                if (fractal.Price <= stat.PriceTake)
                {
                    return (stat with
                    {
                        IndexTotal = index,
                        IndexOpen = indexOpen,
                        IndexTake = index,
                        ResultTake = TakePoint,
                    }, true);
                }

                var takePoint = (stat.PriceOpen - fractal.Price) / Point;
                return (stat with
                {
                    IndexTotal = index,
                    IndexOpen = indexOpen,
                    ResultTake = (int)takePoint,
                }, false);
            }

            return (stat with { IndexTotal = index, IndexOpen = indexOpen }, false);
        }
    }

    /// <summary>
    /// Округление до указанного числа знаков (аналог Python <c>round(x, digits)</c>).
    /// </summary>
    private static double RoundTo(double value, int digits) =>
        Math.Round(value, digits, MidpointRounding.ToEven);

    public override string ToString() =>
        $"StatCursor({Symbol} point={Point} digits={Digits} stop={StopPoint} take={TakePoint})";
}
