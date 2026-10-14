using ArtasTools.MemReport.Analysis;
using Xunit;

namespace ArtasTools.MemReport.Tests.Analysis;

public class TrendAnalyzerTests
{
    [Fact]
    public void Analyze_LinearGrowth_ReturnsSlopePerHour()
    {
        // Рост на 100 МБ каждый час (3600 с), 6 точек.
        var points = new List<(long, double)>();
        for (int h = 0; h < 6; h++)
            points.Add((h * 3600L, h * 100.0));

        var trend = TrendAnalyzer.Analyze(points);

        Assert.Equal(100.0, trend.SlopePerHour, 3);
        Assert.Equal(1.0, trend.R2, 3);
        Assert.Equal(6, trend.Samples);
        Assert.Equal(0.0, trend.First, 3);
        Assert.Equal(500.0, trend.Last, 3);
    }

    [Fact]
    public void Analyze_FlatSeries_SlopeIsZero()
    {
        var points = new List<(long, double)>
        {
            (0, 50), (3600, 50), (7200, 50), (10800, 50)
        };

        var trend = TrendAnalyzer.Analyze(points);

        Assert.Equal(0.0, trend.SlopePerHour, 3);
    }

    [Fact]
    public void Analyze_DecreasingSeries_SlopeNegative()
    {
        var points = new List<(long, double)>
        {
            (0, 500), (3600, 400), (7200, 300), (10800, 200)
        };

        var trend = TrendAnalyzer.Analyze(points);

        Assert.True(trend.SlopePerHour < 0);
        Assert.Equal(-100.0, trend.SlopePerHour, 3);
    }

    [Fact]
    public void Analyze_SinglePoint_ReturnsZeroSlope()
    {
        var points = new List<(long, double)> { (0, 42) };

        var trend = TrendAnalyzer.Analyze(points);

        Assert.Equal(0.0, trend.SlopePerHour);
        Assert.Equal(1, trend.Samples);
        Assert.Equal(42, trend.Min);
        Assert.Equal(42, trend.Max);
    }

    [Fact]
    public void Analyze_EmptySeries_ReturnsZeros()
    {
        var trend = TrendAnalyzer.Analyze(new List<(long, double)>());

        Assert.Equal(0, trend.Samples);
        Assert.Equal(0.0, trend.SlopePerHour);
    }

    [Fact]
    public void Analyze_ComputesMinMax()
    {
        var points = new List<(long, double)>
        {
            (0, 10), (3600, 90), (7200, 30), (10800, 70)
        };

        var trend = TrendAnalyzer.Analyze(points);

        Assert.Equal(10, trend.Min);
        Assert.Equal(90, trend.Max);
    }

    [Fact]
    public void Analyze_ComputesDurationAndTimestamps()
    {
        var points = new List<(long, double)>
        {
            (1000, 1), (4600, 2), (8200, 3)
        };

        var trend = TrendAnalyzer.Analyze(points);

        Assert.Equal(1000, trend.FirstTimestamp);
        Assert.Equal(8200, trend.LastTimestamp);
        Assert.Equal(7200, trend.DurationSeconds);
    }

    [Fact]
    public void IsReliable_FalseWhenTooFewSamplesOrTooShort()
    {
        var points = new List<(long, double)> { (0, 1), (60, 2), (120, 3) };
        var trend = TrendAnalyzer.Analyze(points);

        Assert.False(trend.IsReliable(minSamples: 30, minDurationSeconds: 3600));
        Assert.True(trend.IsReliable(minSamples: 3, minDurationSeconds: 60));
    }

    [Fact]
    public void AnalyzeBaseline_RisingMinima_DetectsGrowth()
    {
        // «Пила» сборок: пики растут, но и минимумы (post-GC) растут на 10/час.
        var points = new List<(long, double)>
        {
            (0, 10), (600, 20), (1200, 12), (1800, 24),
            (2400, 14), (3000, 28), (3600, 16), (4200, 32),
            (4800, 18), (5400, 36), (6000, 20), (6600, 40)
        };

        var (slope, r2, samples) = TrendAnalyzer.AnalyzeBaseline(points);

        Assert.True(slope > 0, $"Ожидался рост baseline, получено {slope}");
        Assert.True(samples >= 3);
    }

    [Fact]
    public void AnalyzeBaseline_FlatMinima_NoGrowth()
    {
        // Пила с постоянной нижней огибающей — утечки нет.
        var points = new List<(long, double)>
        {
            (0, 10), (600, 30), (1200, 10), (1800, 30),
            (2400, 10), (3000, 30), (3600, 10), (4200, 30),
            (4800, 10), (5400, 30)
        };

        var (slope, _, _) = TrendAnalyzer.AnalyzeBaseline(points);

        Assert.Equal(0.0, slope, 3);
    }

    [Fact]
    public void AnalyzeBaseline_TooFewPoints_ReturnsZero()
    {
        var points = new List<(long, double)> { (0, 1), (60, 2) };

        var (slope, r2, samples) = TrendAnalyzer.AnalyzeBaseline(points);

        Assert.Equal(0.0, slope);
        Assert.Equal(0.0, r2);
        Assert.Equal(0, samples);
    }
}
