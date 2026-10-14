using System.Threading.Channels;
using ArtasWriter.Application.Pipeline;
using ArtasCore.Models;
using ArtasWriter.Infrastructure.Services;
using Xunit;

namespace ArtasWriter.Tests.Infrastructure;

/// <summary>
/// Тесты <see cref="WriteBatcher"/>: накопление по размеру и сброс остатка
/// при завершении канала.
/// </summary>
public class WriteBatcherTests
{
    private static FractalRecord Record(long id) =>
        new((int)id, new Fractal(isUp: true, new MyTick { Last = 1.0, TimeMsc = id }));

    [Fact]
    public async Task RunAsync_ReadsUntilChannelCompleted()
    {
        var batcher = new WriteBatcher(batchSize: 100, TimeSpan.FromMilliseconds(10));
        var channel = Channel.CreateUnbounded<FractalRecord>();

        channel.Writer.TryWrite(Record(1));
        channel.Writer.TryWrite(Record(2));
        channel.Writer.TryWrite(Record(3));
        channel.Writer.Complete();

        var flushed = new List<int>();
        await batcher.RunAsync(channel.Reader, (batch, _) =>
        {
            flushed.Add(batch.Count);
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.Equal(3, flushed.Sum());
    }

    [Fact]
    public async Task RunAsync_FlushesByBatchSize()
    {
        // Большой интервал, чтобы сработал размер батча, а не дедлайн.
        var batcher = new WriteBatcher(batchSize: 2, TimeSpan.FromSeconds(30));
        var channel = Channel.CreateUnbounded<FractalRecord>();

        channel.Writer.TryWrite(Record(1));
        channel.Writer.TryWrite(Record(2));
        channel.Writer.TryWrite(Record(3));
        channel.Writer.Complete();

        var batches = new List<int>();
        await batcher.RunAsync(channel.Reader, (batch, _) =>
        {
            batches.Add(batch.Count);
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Первый батч — 2 (по размеру), остаток — 1.
        Assert.Equal(2, batches[0]);
        Assert.Equal(3, batches.Sum());
    }
}
