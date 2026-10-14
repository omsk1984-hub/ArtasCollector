using System.Threading.Channels;
using ArtasWriter.Application.Options;
using ArtasWriter.Application.Pipeline;
using Microsoft.Extensions.Options;

namespace ArtasWriter.Worker;

/// <summary>
/// Создаёт общий канал записи фракталов с настройками backpressure из
/// <see cref="WriterOptions"/>. Единая точка конфигурации канала (SRP).
/// </summary>
public sealed class WriterChannelFactory
{
    private readonly WriterOptions _options;

    public WriterChannelFactory(IOptions<WriterOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Создать bounded-канал для единственного writer-консьюмера.
    /// </summary>
    public Channel<FractalRecord> Create()
    {
        // Один читатель (writer), много писателей (пайплайны инструментов).
        var writerChannelCapacity = _options.WriterChannelCapacity;
        return Channel.CreateBounded<FractalRecord>(new BoundedChannelOptions(writerChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }
}
