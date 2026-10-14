using ArtasWriter.Application.Producers;
using ArtasCore.Abstractions;
using ArtasWriter.FinamApi.Options;
using ArtasWriter.FinamApi.Simulation;
using Microsoft.Extensions.Logging;

namespace ArtasWriter.FinamApi;

/// <summary>
/// Реализация <see cref="IFinamProducerBuilder"/>: создаёт <see cref="FinamProducer"/>
/// для инструмента, используя сконфигурированный транспорт Finam.
/// </summary>
public sealed class FinamProducerBuilder : IFinamProducerBuilder
{
    private readonly IFinamClient? _client;
    private readonly ISimulatedEventSource _simulatedSource;
    private readonly FinamOptions _options;
    private readonly ILoggerFactory _loggerFactory;

    public FinamProducerBuilder(
        FinamOptions options,
        ILoggerFactory loggerFactory,
        ISimulatedEventSource simulatedSource,
        IFinamClient? client = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _simulatedSource = simulatedSource ?? throw new ArgumentNullException(nameof(simulatedSource));
        _client = client;
    }

    /// <inheritdoc />
    public IFinamProducer Create(
        string projectSymbol, string finamSymbol, int channelCapacity)
    {
        return new FinamProducer(
            projectSymbol,
            finamSymbol,
            channelCapacity,
            _client,
            _simulatedSource,
            _options,
            _loggerFactory.CreateLogger($"FinamProducer.{projectSymbol}"));
    }
}
