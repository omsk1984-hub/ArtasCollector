using ArtasCore.Models;
using Xunit;

namespace ArtasWriter.Tests.Core;

/// <summary>
/// Тесты чистого ядра <see cref="FractalFactory"/>.
/// Перенос сценариев из Python tests/unit/core/test_ff_new.py и test_ff_new_simple.py,
/// дополненные проверками детекции фракталов.
/// </summary>
public class FractalFactoryTests
{
    // Символ CNYRUBF: digits=3, trade_tick_size=0.001 (правило проекта).
    private const int Digits = 3;
    private const double TickSize = 0.001;

    [Fact]
    public void Constructor_SetsDistanceFromTickSize()
    {
        var factory = new FractalFactory(Digits, TickSize, distancePoints: 1);

        Assert.Equal(Digits, factory.Digits);
        Assert.Equal(0.001, factory.Distance, precision: 9);
    }

    [Fact]
    public void Constructor_MultipliesDistanceByPoints()
    {
        // 0.001 * 20 = 0.02 (как в Python-тесте)
        var factory = new FractalFactory(Digits, TickSize, distancePoints: 20);

        Assert.Equal(0.02, factory.Distance, precision: 9);
    }

    [Fact]
    public void Init_ResetsState()
    {
        var factory = new FractalFactory(Digits, TickSize);
        var tick = MakeTick(1.000, flags: TickFlag.Buy, timeMsc: 1000);

        factory.Init(tick);

        Assert.True(factory.IsUp);
        Assert.Equal(0.0, factory.Max);
        Assert.Equal(999999999, factory.Min);
        Assert.Same(tick, factory.TickPeak);
    }

    [Fact]
    public void Init_SetsIsInitialized()
    {
        var factory = new FractalFactory(Digits, TickSize);
        Assert.False(factory.IsInitialized);

        factory.Init(MakeTick(1.000, flags: TickFlag.Buy, timeMsc: 1000));

        Assert.True(factory.IsInitialized);
    }

    [Fact]
    public void GetFractal_BeforeInit_ReturnsNull()
    {
        // До инициализации Max/PrevPick равны нулю, что дало бы фрактал
        // с отрицательной дистанцией (0 - price). Такой фрактал не должен
        // формироваться (защита от «холодного старта»).
        var factory = new FractalFactory(Digits, TickSize, distancePoints: 1);

        var fractal = factory.GetFractal(
            MakeTick(86031.0, flags: TickFlag.Sell, timeMsc: 1000));

        Assert.Null(fractal);
    }

    [Fact]
    public void GetFractal_NoMovement_ReturnsNull()
    {
        var factory = new FractalFactory(Digits, TickSize);
        var tick = MakeTick(1.000, flags: TickFlag.Buy, timeMsc: 1000);
        factory.Init(tick);

        var fractal = factory.GetFractal(tick);

        Assert.Null(fractal);
    }

    [Fact]
    public void GetFractal_DetectsDownFractal_WhenUpAndDistanceExceeded()
    {
        // Дистанция 0.02; сначала формируем нижний фрактал при движении вверх.
        var factory = new FractalFactory(Digits, TickSize, distancePoints: 20);

        // Инициализация первым тиком (пик нижний на 1.000 sell).
        var first = MakeTick(last: 1.000, flags: TickFlag.Sell, timeMsc: 1000);
        factory.Init(first);

        // Задаём минимум через продажу.
        factory.Calculate(first);

        // Затем рост покупок выше минимума на > дистанции.
        Fractal? fractal = null;
        for (var i = 1; i <= 30; i++)
        {
            var buy = MakeTick(last: 1.000 + i * 0.001, flags: TickFlag.Buy, timeMsc: 1000 + i);
            fractal = factory.GetFractal(buy);
            if (fractal is not null)
            {
                break;
            }
        }

        Assert.NotNull(fractal);
        Assert.False(fractal!.IsUp); // это нижний фрактал
        Assert.Equal(1.000, fractal.Price, precision: 9);
        Assert.Equal(1000, fractal.Id);
    }

    [Fact]
    public void GetFractal_DetectsUpFractal_AfterDownFractal()
    {
        var factory = new FractalFactory(Digits, TickSize, distancePoints: 20);

        var first = MakeTick(last: 1.000, flags: TickFlag.Sell, timeMsc: 1000);
        factory.Init(first);
        factory.Calculate(first);

        // Формируем нижний фрактал (рост).
        Fractal? down = null;
        for (var i = 1; i <= 30 && down is null; i++)
        {
            var buy = MakeTick(last: 1.000 + i * 0.001, flags: TickFlag.Buy, timeMsc: 1000 + i);
            down = factory.GetFractal(buy);
        }
        Assert.NotNull(down);
        Assert.False(down!.IsUp);

        // Теперь ищем верхний фрактал (падение).
        Fractal? up = null;
        for (var i = 1; i <= 60 && up is null; i++)
        {
            var sell = MakeTick(last: down.Price + 0.030 - i * 0.001, flags: TickFlag.Sell, timeMsc: 2000 + i);
            up = factory.GetFractal(sell);
        }
        Assert.NotNull(up);
        Assert.True(up!.IsUp);
    }

    [Fact]
    public void GetFractal_EmptySequence_NoException()
    {
        var factory = new FractalFactory(Digits, TickSize);
        var tick = MakeTick(1.000, flags: TickFlag.None, timeMsc: 0);

        // Тик без направления — уходит в UpdateAb, не должен падать.
        factory.Init(tick);
        factory.UpdateAb(tick);

        Assert.Null(factory.GetFractal(tick));
    }

    /// <summary>Создать тик с полями, релевантными ядру (Last, Flags, time_msc).</summary>
    private static MyTick MakeTick(double last, TickFlag flags, long timeMsc)
    {
        return new MyTick
        {
            Last = last,
            Flags = (int)flags,
            TimeMsc = timeMsc,
            Volume = 1,
            Ask = last,
            Bid = last,
            MarketPairs = new[]
            {
                new MarketPair(last, 1),
                new MarketPair(last, 1),
            },
        };
    }
}
