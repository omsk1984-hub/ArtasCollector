using System.Threading.Channels;
using ArtasWriter.Application.Pipeline;

namespace ArtasWriter.Infrastructure.Services;

/// <summary>
/// Writer-консьюмер: читает общий канал фракталов и пишет их в хранилище.
/// Абстракция вводится для инверсии зависимостей (host/health не знают
/// о конкретной реализации).
/// </summary>
public interface IFractalWriter
{
    /// <summary>Всего записано фракталов.</summary>
    long TotalWritten { get; }

    /// <summary>Записано фракталов за сегодня.</summary>
    int WrittenToday { get; }

    /// <summary>Цикл чтения из канала до его завершения.</summary>
    Task RunAsync(ChannelReader<FractalRecord> reader, CancellationToken cancellationToken);
}
