using ArtasCore.Abstractions;

namespace ArtasWriter.Application.Producers;

/// <summary>
/// Создаёт экземпляр <see cref="IFinamProducer"/> для конкретного инструмента.
/// Реализация — в слое FinamApi (WebSocket/gRPC).
/// </summary>
public interface IFinamProducerBuilder
{
    /// <summary>
    /// Создать продьюсера для инструмента.
    /// </summary>
    /// <param name="projectSymbol">Символ проекта (например, "Si").</param>
    /// <param name="finamSymbol">Символ Finam (например, "SiZ6").</param>
    /// <param name="channelCapacity">Емкость канала событий.</param>
    IFinamProducer Create(string projectSymbol, string finamSymbol, int channelCapacity);
}
