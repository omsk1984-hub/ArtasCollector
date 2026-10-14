namespace ArtasCore.Models;

/// <summary>
/// Алгоритм обнаружения фракталов на основе расстояния.
/// Чистое ядро, прямой перенос Python <c>FFNew</c> (аналог MQL5 FFNew.mqh).
/// Без I/O и side-effects: только вход <see cref="MyTick"/> → выход <see cref="Fractal"/>.
/// </summary>
public sealed class FractalFactory
{
    /// <summary>Предыдущая выбранная точка (цена последнего фрактала).</summary>
    public double PrevPick { get; set; }

    /// <summary>Текущий максимум.</summary>
    public double Max { get; set; }

    /// <summary>Текущий минимум.</summary>
    public double Min { get; set; } = 999999999;

    /// <summary>
    /// Направление поиска: true — идём снизу вверх (последний фрактал верхний, ловим нижний),
    /// false — наоборот.
    /// </summary>
    public bool IsUp { get; set; } = true;

    /// <summary>Тик пика.</summary>
    public MyTick TickPeak { get; private set; } = new();

    /// <summary>Накопитель для верхнего фрактала.</summary>
    public InfoMeta InfoUp { get; }

    /// <summary>Накопитель для нижнего фрактала.</summary>
    public InfoMeta InfoDown { get; }

    /// <summary>Количество знаков после запятой.</summary>
    public int Digits { get; }

    /// <summary>Дистанция в единицах цены (trade_tick_size * distance_points, округлённая).</summary>
    public double Distance { get; }

    /// <summary>
    /// Признак того, что состояние инициализировано первым тиком (<see cref="Init"/>).
    /// До инициализации <see cref="Max"/> и <see cref="PrevPick"/> равны нулю, что даёт
    /// «мусорные» фракталы (например, отрицательный <c>distance</c>). Пока флаг не
    /// установлен, <see cref="GetFractal"/> не формирует фрактал (аналог прогрева
    /// Python <c>init_fractals()</c>).
    /// </summary>
    public bool IsInitialized { get; private set; }

    /// <param name="digits">Количество знаков после запятой символа.</param>
    /// <param name="tradeTickSize">Минимальный шаг цены (trade_tick_size).</param>
    /// <param name="distancePoints">Дистанция в пунктах (по умолчанию 1).</param>
    public FractalFactory(int digits, double tradeTickSize, int distancePoints = 1)
    {
        Digits = digits;
        // Аналог Python: round(trade_tick_size * distance_points, digits)
        Distance = RoundTo(tradeTickSize * distancePoints, digits);
        InfoUp = new InfoMeta();
        InfoDown = new InfoMeta();
    }

    /// <summary>
    /// Инициализировать алгоритм первым тиком. Аналог Python <c>init(tick_start)</c>.
    /// </summary>
    public void Init(MyTick tickStart)
    {
        ArgumentNullException.ThrowIfNull(tickStart);
        TickPeak = tickStart;
        Max = 0.0;
        Min = 999999999;
        IsUp = true;
        IsInitialized = true;
    }

    /// <summary>
    /// Обновление Ask/Bid. Аналог Python <c>update_ab(tick_prev)</c>.
    /// Вызывается для тиков без направления сделки (IsBuy == null).
    /// </summary>
    public void UpdateAb(MyTick tickPrev)
    {
        ArgumentNullException.ThrowIfNull(tickPrev);
        if (IsUp)
        {
            if (tickPrev.Sell <= Min)
            {
                InfoDown.UpdateGlass(tickPrev, IsUp, Min);
            }
        }
        else
        {
            if (tickPrev.Buy >= Max)
            {
                InfoUp.UpdateGlass(tickPrev, IsUp, Max);
            }
        }
    }

    /// <summary>
    /// Основной расчёт. Аналог Python <c>calculate(tick_prev)</c>.
    /// </summary>
    public void Calculate(MyTick tickPrev)
    {
        ArgumentNullException.ThrowIfNull(tickPrev);
        var buy = tickPrev.Buy;
        var sell = tickPrev.Sell;

        if (IsUp)
        {
            if (buy > Max && tickPrev.IsBuy == true)
            {
                Max = buy;
            }
            if (sell <= Min && tickPrev.IsBuy == false)
            {
                if (sell < Min)
                {
                    InfoDown.OnPickCompleted();
                    Min = sell;
                    Max = Min;
                }
                InfoDown.Add(InfoUp);
                InfoUp.ResetAllState();
                TickPeak = tickPrev;
                InfoDown.UpdateActive(tickPrev, IsUp, Min);
            }
            else
            {
                InfoUp.Update(tickPrev);
                if (sell <= Min)
                {
                    InfoDown.UpdateActive(tickPrev, IsUp, Min);
                }
            }
        }
        else
        {
            if (sell < Min && tickPrev.IsBuy == false)
            {
                Min = sell;
            }
            if (buy >= Max && tickPrev.IsBuy == true)
            {
                if (buy > Max)
                {
                    InfoUp.OnPickCompleted();
                    Max = buy;
                    Min = Max;
                }
                InfoUp.Add(InfoDown);
                InfoDown.ResetAllState();
                TickPeak = tickPrev;
                InfoUp.UpdateActive(tickPrev, IsUp, Max);
            }
            else
            {
                InfoDown.Update(tickPrev);
                if (buy >= Max)
                {
                    InfoUp.UpdateActive(tickPrev, IsUp, Max);
                }
            }
        }
    }

    /// <summary>
    /// Получить фрактал, если расстояние превышено. Аналог Python <c>get_fractal(tick_prev)</c>.
    /// В конце всегда вызывает <see cref="Calculate"/>.
    /// </summary>
    /// <returns>Фрактал или null, если он не сформирован.</returns>
    public Fractal? GetFractal(MyTick tickPrev)
    {
        ArgumentNullException.ThrowIfNull(tickPrev);

        // До инициализации первым тиком Max/PrevPick равны нулю — фрактал получил бы
        // «мусорную» дистанцию (например, 0 - price). Не формируем, пока не прогреты.
        if (!IsInitialized)
        {
            return null;
        }

        Fractal? temp = null;

        if (IsUp)
        {
            var buy = tickPrev.Buy;
            if (tickPrev.IsBuy == true && buy > Max)
            {
                var d = RoundTo(buy - Min, Digits);
                if (d >= Distance)
                {
                    IsUp = false;
                    // Создаём нижний фрактал
                    temp = new Fractal(IsUp, TickPeak);
                    temp.Price = Min;
                    temp.Max = PrevPick;
                    temp.Min = Min;
                    temp.Distance = RoundTo(temp.Max - temp.Min, Digits);
                    temp.Info = InfoF.Create(InfoDown);
                    PrevPick = Min;
                    InfoDown.ResetAllState();
                }
            }
        }
        else
        {
            var sell = tickPrev.Sell;
            if (tickPrev.IsBuy == false && sell < Min)
            {
                var d = RoundTo(Max - sell, Digits);
                if (d >= Distance)
                {
                    IsUp = true;
                    temp = new Fractal(IsUp, TickPeak);
                    temp.Price = Max;
                    temp.Max = Max;
                    temp.Min = PrevPick;
                    temp.Distance = RoundTo(temp.Max - temp.Min, Digits);
                    temp.Info = InfoF.Create(InfoUp);
                    PrevPick = Max;
                    InfoUp.ResetAllState();
                }
            }
        }

        Calculate(tickPrev);
        return temp;
    }

    /// <summary>
    /// Округление до указанного числа знаков (аналог Python <c>round(x, digits)</c>).
    /// </summary>
    private static double RoundTo(double value, int digits) =>
        Math.Round(value, digits, MidpointRounding.ToEven);
}
