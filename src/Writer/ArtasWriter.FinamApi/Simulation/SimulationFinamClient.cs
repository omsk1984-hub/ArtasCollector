using ArtasCore.Abstractions;
using Microsoft.Extensions.Logging;

namespace ArtasWriter.FinamApi.Simulation;

/// <summary>
/// Заглушка <see cref="IFinamClient"/> для режима симуляции: не открывает
/// соединение и не публикует реальных событий. Используется только чтобы
/// DI-граф был валиден, когда <c>Finam:SimulationMode=true</c>.
/// Реальные события в этом режиме генерирует сам <c>FinamProducer</c>.
/// </summary>
public sealed class SimulationFinamClient : IFinamClient
{
    private readonly ILogger<SimulationFinamClient> _logger;

    public SimulationFinamClient(ILogger<SimulationFinamClient> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public event Action<QuoteEvent>? QuoteReceived
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public event Action<OrderBookEvent>? OrderBookReceived
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public event Action<OpenInterestEvent>? OpenInterestReceived
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public event Action<StreamFailedEvent>? StreamFailed
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Finam: клиент в режиме симуляции (подключение не выполняется)");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SubscribeQuotesAsync(string symbol, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task SubscribeOrderBookAsync(string symbol, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task SubscribeOpenInterestAsync(string symbol, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
