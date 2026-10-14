using System.Threading.Channels;
using ArtasCore.Abstractions;
using ArtasCore.Models;
using ArtasWriter.Application.Pipeline;
using ArtasWriter.Application.Producers;
using ArtasWriter.Application.Services;
using ArtasWriter.Infrastructure.Services;
using ArtasWriter.Worker;
using ArtasWriter.Worker.Health;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ArtasWriter.Tests.Worker;

/// <summary>
/// Тесты реакции на отказ стрима Finam: per-symbol finam_connected и общий
/// status=degraded в health, а также агрегат FinamConnected. Список стримов
/// (streams[]) в health-ответ не выводится (см. HealthState).
/// </summary>
public class StreamFailureHealthTests
{
    private static readonly SymbolInfo SiInfo =
        new() { Id = 1, Name = "Si", Symbol = "SiZ6", Digits = 0, Point = 1 };

    /// <summary>Минимальная заглушка IWriterState для теста только состояния стримов.</summary>
    private sealed class TestState : IWriterState
    {
        private readonly List<InstrumentPipeline> _pipelines = new();

        public DateTime StartTimeUtc { get; } = DateTime.UtcNow;
        public bool DbConnected { get; private set; } = true;
        public IReadOnlyList<InstrumentPipeline> Pipelines => _pipelines;

        private readonly Dictionary<string, Dictionary<StreamKind, StreamStatus>> _streams =
            new(StringComparer.Ordinal);

        public bool FinamConnected
        {
            get
            {
                foreach (var byKind in _streams.Values)
                {
                    foreach (var s in byKind.Values)
                    {
                        if (!s.Up) return false;
                    }
                }

                return true;
            }
        }

        public void SetDbConnected(bool connected) => DbConnected = connected;

        public void SetStreamStatus(string finamSymbol, StreamKind kind, bool up, string? reason = null)
        {
            if (!_streams.TryGetValue(finamSymbol, out var byKind))
            {
                byKind = new Dictionary<StreamKind, StreamStatus>();
                _streams[finamSymbol] = byKind;
            }

            byKind[kind] = new StreamStatus(up, up ? null : reason);
        }

        public IReadOnlyDictionary<StreamKind, StreamStatus> GetStreamStatuses(string finamSymbol)
            => _streams.TryGetValue(finamSymbol, out var byKind)
                ? new Dictionary<StreamKind, StreamStatus>(byKind)
                : new Dictionary<StreamKind, StreamStatus>();

        public void SetPipelines(IEnumerable<InstrumentPipeline> pipelines)
        {
            _pipelines.Clear();
            _pipelines.AddRange(pipelines);
        }
    }

    private sealed class FakeWriter : IFractalWriter
    {
        public long TotalWritten => 42;
        public int WrittenToday => 0;
        public Task RunAsync(ChannelReader<FractalRecord> reader, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class FakeProducer : IFinamProducer
    {
        public string ProjectSymbol { get; init; } = "Si";
        public string FinamSymbol { get; init; } = "SiZ6@RTSX";
        public ChannelReader<FinamEvent> Reader => Channel.CreateUnbounded<FinamEvent>().Reader;
        public event Action<StreamFailedEvent>? StreamFailed;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Raise(StreamFailedEvent evt) => StreamFailed?.Invoke(evt);
    }

    private static InstrumentPipeline BuildPipeline(string finamSymbol)
    {
        var tickService = new TickService(SiInfo, bufferSize: 10);
        var writer = Channel.CreateUnbounded<FractalRecord>();
        var pipeline = new FractalPipeline(
            tickService, SiInfo.Id, writer.Writer, NullLogger.Instance);

        return new InstrumentPipeline
        {
            ProjectSymbol = SiInfo.Name,
            FinamSymbol = finamSymbol,
            SymbolInfo = SiInfo,
            Producer = new FakeProducer { ProjectSymbol = SiInfo.Name, FinamSymbol = finamSymbol },
            Pipeline = pipeline,
            Reader = Channel.CreateUnbounded<FinamEvent>().Reader,
        };
    }

    [Fact]
    public void FinamConnected_IsTrue_WhenAllStreamsUp()
    {
        var state = new TestState();
        state.SetStreamStatus("SiZ6@RTSX", StreamKind.Quotes, up: true);
        state.SetStreamStatus("SiZ6@RTSX", StreamKind.OrderBook, up: true);
        state.SetStreamStatus("SiZ6@RTSX", StreamKind.Trades, up: true);

        Assert.True(state.FinamConnected);
    }

    [Fact]
    public void FinamConnected_IsFalse_WhenAnyStreamFailed()
    {
        var state = new TestState();
        state.SetStreamStatus("SiZ6@RTSX", StreamKind.Quotes, up: true);
        state.SetStreamStatus("SiZ6@RTSX", StreamKind.OrderBook, up: true);
        state.SetStreamStatus("SiZ6@RTSX", StreamKind.Trades, up: false, "Invalid access key");

        Assert.False(state.FinamConnected);
    }

    [Fact]
    public void Health_ShowsPerSymbolDisconnected_AndDegradedStatus()
    {
        const string finamSymbol = "SiZ6@RTSX";
        var state = new TestState();
        state.SetPipelines(new[] { BuildPipeline(finamSymbol) });

        // Котировки и стакан живы, сделки упали.
        state.SetStreamStatus(finamSymbol, StreamKind.Quotes, up: true);
        state.SetStreamStatus(finamSymbol, StreamKind.OrderBook, up: true);
        state.SetStreamStatus(finamSymbol, StreamKind.Trades, up: false, "Invalid access key");

        var builder = new HealthSnapshotBuilder(
            state, new FakeWriter(), NullLogger<HealthSnapshotBuilder>.Instance);

        var snapshot = builder.Build();

        Assert.Equal("degraded", snapshot.Status);
        Assert.False(snapshot.FinamConnected);

        var symbol = Assert.Single(snapshot.Symbols);
        Assert.False(symbol.FinamConnected);
    }

    [Fact]
    public void Health_IsOk_WhenAllStreamsUp()
    {
        const string finamSymbol = "SiZ6@RTSX";
        var state = new TestState();
        state.SetPipelines(new[] { BuildPipeline(finamSymbol) });

        state.SetStreamStatus(finamSymbol, StreamKind.Quotes, up: true);
        state.SetStreamStatus(finamSymbol, StreamKind.OrderBook, up: true);
        state.SetStreamStatus(finamSymbol, StreamKind.Trades, up: true);

        var builder = new HealthSnapshotBuilder(
            state, new FakeWriter(), NullLogger<HealthSnapshotBuilder>.Instance);

        var snapshot = builder.Build();

        Assert.Equal("ok", snapshot.Status);
        Assert.True(snapshot.FinamConnected);

        var symbol = Assert.Single(snapshot.Symbols);
        Assert.True(symbol.FinamConnected);
    }

    [Fact]
    public void Health_MarksSymbolConnected_WhenNoStatuses()
    {
        const string finamSymbol = "SiZ6@RTSX";
        var state = new TestState();
        state.SetPipelines(new[] { BuildPipeline(finamSymbol) });

        var builder = new HealthSnapshotBuilder(
            state, new FakeWriter(), NullLogger<HealthSnapshotBuilder>.Instance);

        var snapshot = builder.Build();

        Assert.Equal("ok", snapshot.Status);
        var symbol = Assert.Single(snapshot.Symbols);
        Assert.True(symbol.FinamConnected);
    }
}
