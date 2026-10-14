using System.Threading.Channels;
using ArtasCore.Abstractions;
using ArtasWriter.Application.Pipeline;
using ArtasWriter.Application.Producers;
using ArtasWriter.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace ArtasWriter.Worker;

/// <summary>
/// Оркестратор конвейера: создаёт общий канал записи, строит конвейеры инструментов,
/// запускает единственного writer-консьюмера, потребителей и продьюсеров, а также
/// выполняет их согласованную остановку. Вынесен из hosted service (SRP).
/// </summary>
public sealed class PipelineOrchestrator
{
    private readonly IFinamProducerFactory _producerFactory;
    private readonly IFractalWriter _writer;
    private readonly WriterChannelFactory _channelFactory;
    private readonly IWriterState _state;
    private readonly ILogger<PipelineOrchestrator> _logger;

    // Запущенные задачи потребителей и продьюсеров для последующего ожидания.
    private readonly List<Task> _consumerTasks = new();
    private Task? _writerTask;
    private ChannelWriter<FractalRecord>? _writeChannelWriter;
    private IReadOnlyList<InstrumentPipeline> _pipelines = Array.Empty<InstrumentPipeline>();

    public PipelineOrchestrator(
        IFinamProducerFactory producerFactory,
        IFractalWriter writer,
        WriterChannelFactory channelFactory,
        IWriterState state,
        ILogger<PipelineOrchestrator> logger)
    {
        _producerFactory = producerFactory ?? throw new ArgumentNullException(nameof(producerFactory));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _channelFactory = channelFactory ?? throw new ArgumentNullException(nameof(channelFactory));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Создать канал, конвейеры, writer и продьюсеры; вернуть число запущенных конвейеров.
    /// </summary>
    public async Task<int> StartAsync(CancellationToken cancellationToken)
    {
        var writeChannel = _channelFactory.Create();
        _writeChannelWriter = writeChannel.Writer;

        // Конвейеры по инструментам.
        var pipelines = await _producerFactory
            .CreateAsync(writeChannel.Writer, cancellationToken);

        if (pipelines.Count == 0)
        {
            _logger.LogError(
                "Не создано ни одного конвейера — проверьте секцию Writer:Symbols и таблицу symbols");
        }

        _pipelines = pipelines;
        _state.SetPipelines(pipelines);

        // Единственный writer-консьюмер.
        _writerTask = Task.Run(
            () => _writer.RunAsync(writeChannel.Reader, cancellationToken), cancellationToken);

        // Потребители инструментов.
        _consumerTasks.Clear();
        _consumerTasks.AddRange(pipelines.Select(
            p => Task.Run(() => p.Pipeline.RunAsync(p.Reader, cancellationToken), cancellationToken)));

        // Продьюсеры.
        foreach (var p in pipelines)
        {
            await p.Producer.StartAsync(cancellationToken);

            // Стартовые статусы стримов символа: все активны. Дальнейшие
            // изменения приходят из клиента через событие StreamFailed
            // (см. FinamProducer → IWriterState).
            _state.SetStreamStatus(p.FinamSymbol, StreamKind.Quotes, up: true);
            _state.SetStreamStatus(p.FinamSymbol, StreamKind.OrderBook, up: true);
            _state.SetStreamStatus(p.FinamSymbol, StreamKind.Trades, up: true);

            // Проброс отказа стрима в состояние (health: finam_connected=false
            // на уровне символа и агрегированно). Символ уже известен из события.
            p.Producer.StreamFailed += evt =>
                _state.SetStreamStatus(evt.Symbol, evt.Kind, up: false, reason: evt.Reason);
        }

        return pipelines.Count;
    }

    /// <summary>
    /// Согласованная остановка: продьюсеры → завершение канала → ожидание
    /// потребителей и writer.
    /// </summary>
    public async Task StopAsync()
    {
        // Останавливаем продьюсеры (у каждого свой канал событий).
        foreach (var p in _pipelines)
        {
            await p.Producer.StopAsync(CancellationToken.None);
        }

        // Завершаем общий канал записи и дожидаемся потребителей и writer.
        _writeChannelWriter?.TryComplete();

        try
        {
            var toAwait = _consumerTasks.ToList();
            if (_writerTask is not null)
            {
                toAwait.Add(_writerTask);
            }

            if (toAwait.Count > 0)
            {
                await Task.WhenAll(toAwait);
            }
        }
        catch (OperationCanceledException)
        {
            // ожидаемо
        }
    }
}
