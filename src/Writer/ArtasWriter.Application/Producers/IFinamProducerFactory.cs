using System.Threading.Channels;
using ArtasWriter.Application.Pipeline;
using ArtasCore.Models;

namespace ArtasWriter.Application.Producers;

/// <summary>
/// Готовая к запуску пара «продьюсер + пайплайн» на один инструмент.
/// </summary>
public sealed class InstrumentPipeline
{
    public required string ProjectSymbol { get; init; }
    public required string FinamSymbol { get; init; }
    public required SymbolInfo SymbolInfo { get; init; }
    public required IFinamProducer Producer { get; init; }
    public required FractalPipeline Pipeline { get; init; }
    public required ChannelReader<FinamEvent> Reader { get; init; }
}

/// <summary>
/// Фабрика продьюсеров: на каждый инструмент создаёт продьюсера, персональный
/// канал и пайплайн-потребитель; связывает их с общим каналом записи фракталов.
/// </summary>
public interface IFinamProducerFactory
{
    /// <summary>
    /// Создать конвейеры для всех инструментов из настроек.
    /// </summary>
    /// <param name="writerChannel">Общий канал записи фракталов (один writer на процесс).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task<IReadOnlyList<InstrumentPipeline>> CreateAsync(
        ChannelWriter<FractalRecord> writerChannel,
        CancellationToken cancellationToken = default);
}
