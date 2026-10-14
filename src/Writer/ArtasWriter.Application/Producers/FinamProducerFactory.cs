using System.Threading.Channels;
using ArtasWriter.Application.Options;
using ArtasWriter.Application.Pipeline;
using ArtasCore.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArtasWriter.Application.Producers;

/// <summary>
/// Реализация <see cref="IFinamProducerFactory"/>.
/// На каждый инструмент из настроек: резолвит имя в БД, создаёт продьюсера,
/// персональный bounded-канал и пайплайн-потребитель. Создание пайплайна
/// делегировано <see cref="IInstrumentPipelineFactory"/>.
/// </summary>
public sealed class FinamProducerFactory : IFinamProducerFactory
{
    private readonly IFinamProducerBuilder _producerBuilder;
    private readonly IInstrumentPipelineFactory _pipelineFactory;
    private readonly ISymbolRepository _symbolRepository;
    private readonly WriterOptions _options;
    private readonly ILogger<FinamProducerFactory> _logger;

    public FinamProducerFactory(
        IFinamProducerBuilder producerBuilder,
        IInstrumentPipelineFactory pipelineFactory,
        ISymbolRepository symbolRepository,
        IOptions<WriterOptions> options,
        ILogger<FinamProducerFactory> logger)
    {
        _producerBuilder = producerBuilder ?? throw new ArgumentNullException(nameof(producerBuilder));
        _pipelineFactory = pipelineFactory ?? throw new ArgumentNullException(nameof(pipelineFactory));
        _symbolRepository = symbolRepository ?? throw new ArgumentNullException(nameof(symbolRepository));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InstrumentPipeline>> CreateAsync(
        ChannelWriter<FractalRecord> writerChannel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writerChannel);

        var result = new List<InstrumentPipeline>();

        foreach (var setting in _options.Symbols)
        {
            if (string.IsNullOrWhiteSpace(setting.Name))
            {
                continue;
            }

            // Имя из настроек совпадает с public.symbols.name (например "Si").
            var symbolInfo = await _symbolRepository
                .GetByNameAsync(setting.Name, cancellationToken);

            if (symbolInfo is null)
            {
                _logger.LogWarning(
                    "Символ {Name} не найден в public.symbols (по name) — пропускаю", setting.Name);
                continue;
            }

            // Код для соединения с Finam берём из колонки symbol (контракт, например "SiZ6").
            var finamSymbol = symbolInfo.Symbol;
            if (string.IsNullOrWhiteSpace(finamSymbol))
            {
                _logger.LogWarning(
                    "Для символа {Name} пуста колонка symbol — пропускаю", setting.Name);
                continue;
            }

            var producer = _producerBuilder.Create(
                setting.Name, finamSymbol, _options.ProducerChannelCapacity);

            var pipeline = await _pipelineFactory.CreateAsync(
                symbolInfo, writerChannel, setting.Name, cancellationToken);

            var wrapper = new InstrumentPipeline
            {
                ProjectSymbol = setting.Name,
                FinamSymbol = finamSymbol,
                SymbolInfo = symbolInfo,
                Producer = producer,
                Pipeline = pipeline,
                // Продьюсер владеет собственным каналом; потребитель читает из него.
                Reader = producer.Reader,
            };

            result.Add(wrapper);
            _logger.LogInformation(
                "Создан конвейер: {Name} → Finam {FinamSymbol} (symbol_id={Id})",
                setting.Name, finamSymbol, symbolInfo.Id);
        }

        return result;
    }
}
