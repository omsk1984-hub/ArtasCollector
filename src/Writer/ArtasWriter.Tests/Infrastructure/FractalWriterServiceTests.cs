using System.Threading.Channels;
using ArtasWriter.Application.Options;
using ArtasWriter.Application.Pipeline;
using ArtasCore.Abstractions;
using ArtasCore.Models;
using ArtasWriter.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ArtasWriter.Tests.Infrastructure;

/// <summary>
/// Тесты <see cref="FractalWriterService"/> на мок-репозитории.
/// </summary>
public class FractalWriterServiceTests
{
    private sealed class MockFractalRepository : IFractalRepository
    {
        public List<(int SymbolId, Fractal Fractal)> Saved { get; } = new();
        public int BatchCalls { get; private set; }

        public Task<bool> SaveAsync(int symbolId, Fractal fractal, CancellationToken ct = default)
        {
            Saved.Add((symbolId, fractal));
            return Task.FromResult(true);
        }

        public Task<int> SaveBatchAsync(
            IReadOnlyList<(int SymbolId, Fractal Fractal)> records, CancellationToken ct = default)
        {
            BatchCalls++;
            Saved.AddRange(records);
            return Task.FromResult(records.Count);
        }

        public Task<int> CountTodayAsync(int symbolId, CancellationToken ct = default) =>
            Task.FromResult(0);

        public Task<long?> GetDayStartInterestAsync(int symbolId, CancellationToken ct = default) =>
            Task.FromResult<long?>(null);
    }

    private static Fractal MakeFractal(long id) =>
        new(isUp: true, new MyTick { Last = 1.0, TimeMsc = id });

    private static FractalWriterService Create(MockFractalRepository repo, int batchSize = 100)
    {
        var options = Options.Create(new WriterOptions
        {
            WriteBatchSize = batchSize,
            WriteFlushIntervalMs = 50,
        });
        return new FractalWriterService(repo, options, NullLogger<FractalWriterService>.Instance);
    }

    [Fact]
    public async Task FlushAsync_EmptyBatch_NoWrite()
    {
        var repo = new MockFractalRepository();
        var service = Create(repo);

        var result = await service.FlushAsync(new List<FractalRecord>(), CancellationToken.None);

        Assert.Equal(0, result);
        Assert.Empty(repo.Saved);
    }

    [Fact]
    public async Task FlushAsync_WritesBatchAndUpdatesCounters()
    {
        var repo = new MockFractalRepository();
        var service = Create(repo);
        var batch = new List<FractalRecord>
        {
            new(1, MakeFractal(100)),
            new(2, MakeFractal(200)),
        };

        var result = await service.FlushAsync(batch, CancellationToken.None);

        Assert.Equal(2, result);
        Assert.Equal(2, repo.Saved.Count);
        Assert.Equal(2, service.TotalWritten);
        Assert.Equal(2, service.WrittenToday);
    }

    [Fact]
    public async Task RunAsync_ReadsChannelUntilCompleted()
    {
        var repo = new MockFractalRepository();
        var service = Create(repo);
        var channel = Channel.CreateUnbounded<FractalRecord>();

        channel.Writer.TryWrite(new FractalRecord(1, MakeFractal(1)));
        channel.Writer.TryWrite(new FractalRecord(1, MakeFractal(2)));
        channel.Writer.Complete();

        await service.RunAsync(channel.Reader, CancellationToken.None);

        Assert.Equal(2, repo.Saved.Count);
        Assert.Equal(2, service.TotalWritten);
    }
}
