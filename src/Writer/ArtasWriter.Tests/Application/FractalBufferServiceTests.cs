using ArtasWriter.Application.Services;
using ArtasCore.Models;
using Xunit;

namespace ArtasWriter.Tests.Application;

/// <summary>
/// Тесты <see cref="FractalBufferService"/>: инициализация базы интереса дня
/// извне (из БД при рестарте Writer) и fallback на первый фрактал дня.
///
/// ВАЖНО: день определяется по UTC (<c>TickPeak.TimeUtc</c>), а не по МСК.
/// МСК (UTC+3) используется только для визуализации.
/// </summary>
public class FractalBufferServiceTests
{
    private static readonly DateOnly Day = new(2026, 10, 12);

    /// <summary>
    /// Построить фрактал с временем пика в заданный UTC-день.
    /// <paramref name="utcDay"/> — календарный день ПО UTC (не МСК).
    /// </summary>
    private static Fractal MakeFractal(long id, long interest, bool isUp, DateTime utcDay)
    {
        // К базовому моменту UTC-дня прибавляем id секунд, чтобы у фракталов
        // одного дня были разные возрастающие Id. TimeMsc трактуется как UTC.
        var utcTime = utcDay + TimeSpan.FromSeconds(id);
        var unixMs = new DateTimeOffset(DateTime.SpecifyKind(utcTime, DateTimeKind.Utc))
            .ToUnixTimeMilliseconds();

        return new Fractal(isUp, new MyTick { TimeMsc = unixMs, Interest = interest })
        {
            Id = unixMs,
            TickPeak = new MyTick { TimeMsc = unixMs, Interest = interest },
        };
    }

    [Fact]
    public void SetDayStartInterest_UsesExternalBase()
    {
        var service = new FractalBufferService();
        var utcDay = new DateTime(2026, 10, 12, 12, 0, 0);

        // База дня из БД — интерес раннего фрактала дня (например, 5000).
        service.SetDayStartInterest(Day, 5000);

        var fractal = MakeFractal(1, 5050, isUp: true, utcDay);

        Assert.True(service.AddFractal(fractal));
        // FromStartInterest = Interest − база дня = 5050 − 5000 = 50.
        Assert.Equal(50, fractal.FromStartInterest);
    }

    [Fact]
    public void WithoutExternalBase_UsesFirstFractalOfDay()
    {
        var service = new FractalBufferService();
        var utcDay = new DateTime(2026, 10, 12, 12, 0, 0);

        var first = MakeFractal(1, 5000, isUp: true, utcDay);
        var second = MakeFractal(2, 5050, isUp: false, utcDay);

        Assert.True(service.AddFractal(first));
        Assert.True(service.AddFractal(second));

        // База — интерес первого фрактала дня: 5000.
        Assert.Equal(0, first.FromStartInterest);
        Assert.Equal(50, second.FromStartInterest);
    }

    [Fact]
    public void ExternalBase_AppliedToFirstFractalAfterRestart()
    {
        var service = new FractalBufferService();
        var utcDay = new DateTime(2026, 10, 12, 14, 0, 0);

        // Writer перезапущен среди дня: база из БД = 5000, первый полученный
        // после рестарта фрактал имеет интерес 5120 — FromStartInterest = 120.
        service.SetDayStartInterest(Day, 5000);

        var fractal = MakeFractal(1, 5120, isUp: true, utcDay);
        Assert.True(service.AddFractal(fractal));
        Assert.Equal(120, fractal.FromStartInterest);
    }

    [Fact]
    public void SetDayStartInterest_ResetsOnNewDay()
    {
        var service = new FractalBufferService();
        var day1 = new DateTime(2026, 10, 12, 12, 0, 0);
        var day2 = new DateTime(2026, 10, 13, 12, 0, 0);

        service.SetDayStartInterest(Day, 5000);
        var f1 = MakeFractal(1, 5100, isUp: true, day1);
        Assert.True(service.AddFractal(f1));
        Assert.Equal(100, f1.FromStartInterest);

        // Новый день: база пересчитывается от первого фрактала нового дня.
        var f2 = MakeFractal(2, 7000, isUp: false, day2);
        Assert.True(service.AddFractal(f2));
        Assert.Equal(0, f2.FromStartInterest);

        var f3 = MakeFractal(3, 7080, isUp: true, day2);
        Assert.True(service.AddFractal(f3));
        Assert.Equal(80, f3.FromStartInterest);
    }

    /// <summary>
    /// Граница суток по UTC: последний фрактал дня и первый фрактал следующего
    /// UTC-дня. База должна сброситься на первом фрактале нового UTC-дня
    /// (FromStartInterest = 0), а не тянуться из предыдущего дня.
    ///
    /// Это регрессионный тест дефекта: раньше день определялся по МСК
    /// (<c>TimeUtc + 3h</c>), из-за чего первый фрактал нового дня у источника
    /// получал базу от предыдущего дня.
    /// </summary>
    [Fact]
    public void DayBoundary_ByUtc_ResetsBaseAtMidnight()
    {
        var service = new FractalBufferService();

        // Последний фрактал 12.10 по UTC: 23:59:59 UTC.
        var lastOfDay1 = MakeFractal(1, 9000, isUp: true, new DateTime(2026, 10, 12, 23, 59, 0));

        // Первый фрактал 13.10 по UTC: 00:00:01 UTC.
        var firstOfDay2 = MakeFractal(2, 9500, isUp: false, new DateTime(2026, 10, 13, 0, 0, 0));

        Assert.True(service.AddFractal(lastOfDay1));
        // База взята из первого фрактала дня → FSI = 0.
        Assert.Equal(0, lastOfDay1.FromStartInterest);

        Assert.True(service.AddFractal(firstOfDay2));
        // Новый UTC-день → база пересчитана → FSI = 0 (а не 500 из старого дня).
        Assert.Equal(0, firstOfDay2.FromStartInterest);
    }
}
