using ArtasWriter.Infrastructure.Services;
using Xunit;

namespace ArtasWriter.Tests.Infrastructure;

/// <summary>
/// Тесты <see cref="DailyCounter"/>: накопление и сброс при смене дня.
/// </summary>
public class DailyCounterTests
{
    [Fact]
    public void Add_AccumulatesWithinSameDay()
    {
        var now = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var counter = new DailyCounter(() => now);

        counter.Add(5);
        counter.Add(3);

        Assert.Equal(8, counter.WrittenToday);
    }

    [Fact]
    public void Add_ResetsOnNewDay()
    {
        var now = new DateTime(2026, 1, 1, 23, 0, 0, DateTimeKind.Utc);
        var counter = new DailyCounter(() => now);

        counter.Add(10);
        Assert.Equal(10, counter.WrittenToday);

        now = now.AddDays(1);
        counter.Add(2);

        Assert.Equal(2, counter.WrittenToday);
    }
}
