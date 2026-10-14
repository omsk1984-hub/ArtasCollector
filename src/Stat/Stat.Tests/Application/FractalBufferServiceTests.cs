using ArtasCore.Models;
using Stat.Application.Buffers;
using Stat.Application.Services;
using Xunit;

namespace Stat.Tests.Application;

/// <summary>
/// Тесты семантики циклического буфера и сервиса буфера фракталов,
/// критичной для stat-расчёта (<c>Count</c> и <c>get_by_index</c>).
/// </summary>
public sealed class FractalBufferServiceTests
{
    private static Fractal MakeFractal(long id, bool isUp, double price, long interest = 0, int hour = 10)
    {
        var tick = new MyTick
        {
            TimeMsc = new DateTimeOffset(2026, 1, 2, hour, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            Interest = interest,
        };

        return new Fractal(isUp, tick)
        {
            Id = id,
            Price = price,
        };
    }

    [Fact]
    public void AddFractal_AssignsSequentialIndex()
    {
        var service = new FractalBufferService(bufferSize: 500);

        service.AddFractal(MakeFractal(1, true, 10.0));
        service.AddFractal(MakeFractal(2, false, 9.0));
        service.AddFractal(MakeFractal(3, true, 11.0));

        Assert.Equal(3, service.FractalsCount);
        Assert.Equal(3, service.FractalCurrent!.Index);
        Assert.Equal(3, service.LastId);
    }

    [Fact]
    public void AddFractal_SkipsSameDirection()
    {
        var service = new FractalBufferService();

        Assert.True(service.AddFractal(MakeFractal(1, true, 10.0)));
        Assert.False(service.AddFractal(MakeFractal(2, true, 11.0)));
        Assert.Equal(1, service.FractalsCount);
    }

    [Fact]
    public void AddFractal_SkipsNonIncreasingId()
    {
        var service = new FractalBufferService();

        service.AddFractal(MakeFractal(5, true, 10.0));
        Assert.False(service.AddFractal(MakeFractal(5, false, 9.0)));
        Assert.Equal(1, service.FractalsCount);
    }

    [Fact]
    public void Count_GrowsBeyondCapacity()
    {
        var buffer = new CircularBuffer<int>(capacity: 3);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);
        buffer.Add(4); // вытесняет 1

        Assert.Equal(4, buffer.Count);
        Assert.Equal(3, buffer.LiveCount);
    }

    [Fact]
    public void GetByIndex_MatchesPythonSemanticsWithinWindow()
    {
        var buffer = new CircularBuffer<int>(capacity: 5);
        for (var i = 1; i <= 7; i++)
        {
            buffer.Add(i);
        }

        // Python: get_by_index(index) = self[Count - index - 1]; при new_index >= Size -> None.
        // Count=7, capacity=5: валидны только new_index < 5, т.е. index >= 3.
        Assert.Equal(7, buffer.Count);

        // Живое окно (последние 5): [3,4,5,6,7]; self[offset] — offset назад от 7.
        // index=4 -> new_index=2 -> self[2] -> 5.
        Assert.Equal(5, buffer.GetByIndex(4));
        // index=3 -> new_index=3 -> self[3] -> 4.
        Assert.Equal(4, buffer.GetByIndex(3));
        // index=0 -> new_index=6 (>= capacity) -> default (Python None).
        Assert.Equal(default, buffer.GetByIndex(0));
    }

    [Fact]
    public void GetByIndex_WithoutOverflow_ReturnsOldest()
    {
        var buffer = new CircularBuffer<int>(capacity: 5);
        buffer.Add(10);
        buffer.Add(20);
        buffer.Add(30);

        // Count=3: get_by_index(0) -> само старейшее (10).
        Assert.Equal(10, buffer.GetByIndex(0));
        Assert.Equal(20, buffer.GetByIndex(1));
        Assert.Equal(30, buffer.GetByIndex(2));
    }

    [Fact]
    public void Callback_ReceivesBufferSnapshot()
    {
        var service = new FractalBufferService();
        CircularBuffer<Fractal>? captured = null;
        var callbackCount = 0;

        service.AddFractalCallback((_, buffer) =>
        {
            captured = buffer;
            callbackCount++;
        });

        service.AddFractal(MakeFractal(1, true, 10.0));
        service.AddFractal(MakeFractal(2, false, 9.0));

        Assert.Equal(2, callbackCount);
        Assert.NotNull(captured);
        Assert.Equal(2, captured!.Count);
    }
}
