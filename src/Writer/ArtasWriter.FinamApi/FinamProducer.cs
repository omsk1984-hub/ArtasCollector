using System.Threading.Channels;
using ArtasWriter.Application.Producers;
using ArtasCore.Abstractions;
using ArtasWriter.FinamApi.Options;
using ArtasWriter.FinamApi.Simulation;
using Microsoft.Extensions.Logging;

namespace ArtasWriter.FinamApi;

/// <summary>
/// Продьюсер Finam: подписывается на стрим и публикует события в свой канал.
/// Если реальный клиент не сконфигурирован или включён режим симуляции —
/// делегирует генерацию синтетических событий <see cref="ISimulatedEventSource"/>.
/// </summary>
public sealed class FinamProducer : IFinamProducer
{
    private readonly Channel<FinamEvent> _channel;
    private readonly IFinamClient? _client;
    private readonly ISimulatedEventSource _simulatedSource;
    private readonly FinamOptions _options;
    private readonly ILogger _logger;
    private CancellationTokenSource? _cts;
    private Task? _pumpTask;

    public FinamProducer(
        string projectSymbol,
        string finamSymbol,
        int channelCapacity,
        IFinamClient? client,
        ISimulatedEventSource simulatedSource,
        FinamOptions options,
        ILogger logger)
    {
        ProjectSymbol = projectSymbol;
        // Полный символ в терминах Finam: ticker@mic (например "SiZ6@RTSX").
        FinamSymbol = options.BuildFullSymbol(finamSymbol);
        _client = client;
        _simulatedSource = simulatedSource ?? throw new ArgumentNullException(nameof(simulatedSource));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _channel = Channel.CreateBounded<FinamEvent>(new BoundedChannelOptions(channelCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
    }

    public string ProjectSymbol { get; }
    public string FinamSymbol { get; }
    public ChannelReader<FinamEvent> Reader => _channel.Reader;

    /// <inheritdoc />
    public event Action<StreamFailedEvent>? StreamFailed;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (_options.SimulationMode || _client is null)
        {
            _logger.LogWarning(
                "Продьюсер {Symbol}: режим симуляции (реальный клиент Finam не сконфигурирован)",
                FinamSymbol);
            _pumpTask = Task.Run(
                () => _simulatedSource.RunAsync(
                    FinamSymbol, _channel.Writer.TryWrite, _cts.Token),
                _cts.Token);
            return;
        }

        _client.QuoteReceived += OnQuote;
        _client.OrderBookReceived += OnOrderBook;
        _client.OpenInterestReceived += OnOpenInterest;
        _client.StreamFailed += OnStreamFailed;

        await _client.ConnectAsync(_cts.Token);
        // Открытый интерес подписываем РАНЬШЕ котировок: иначе первые фракталы
        // могут сформироваться до прихода первого OpenInterestEvent и попасть
        // в БД с Interest = 0 (см. Python tick_service.py, где current_markets
        // обновляют до обработки тиков по той же причине).
        await _client.SubscribeOpenInterestAsync(FinamSymbol, _cts.Token);
        await _client.SubscribeOrderBookAsync(FinamSymbol, _cts.Token);
        await _client.SubscribeQuotesAsync(FinamSymbol, _cts.Token);

        _logger.LogInformation("Продьюсер {Symbol} подписан на Finam", FinamSymbol);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            _client.QuoteReceived -= OnQuote;
            _client.OrderBookReceived -= OnOrderBook;
            _client.OpenInterestReceived -= OnOpenInterest;
            _client.StreamFailed -= OnStreamFailed;
        }

        if (_cts is not null)
        {
            await _cts.CancelAsync();
        }

        if (_pumpTask is not null)
        {
            try
            {
                await _pumpTask;
            }
            catch (OperationCanceledException)
            {
                // ожидаемо
            }
        }

        _channel.Writer.TryComplete();
    }

    private void OnQuote(QuoteEvent evt)
    {
        if (evt.Symbol == FinamSymbol)
        {
            _channel.Writer.TryWrite(new FinamEvent(evt));
        }
    }

    private void OnOrderBook(OrderBookEvent evt)
    {
        if (evt.Symbol == FinamSymbol)
        {
            _channel.Writer.TryWrite(new FinamEvent(evt));
        }
    }

    private void OnOpenInterest(OpenInterestEvent evt)
    {
        if (evt.Symbol == FinamSymbol)
        {
            _channel.Writer.TryWrite(new FinamEvent(evt));
        }
    }

    /// <summary>
    /// Ретрансляция отказа стрима наружу. Событие клиента общее для всех
    /// символов, поэтому фильтруем по своему <see cref="FinamSymbol"/>.
    /// </summary>
    private void OnStreamFailed(StreamFailedEvent evt)
    {
        if (evt.Symbol != FinamSymbol)
        {
            return;
        }

        try
        {
            StreamFailed?.Invoke(evt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Продьюсер {Symbol}: ошибка обработчика StreamFailed", FinamSymbol);
        }
    }
}
