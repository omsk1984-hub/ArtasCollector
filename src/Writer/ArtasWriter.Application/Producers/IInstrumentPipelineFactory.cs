using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ArtasWriter.Application.Pipeline;
using ArtasCore.Models;

namespace ArtasWriter.Application.Producers;

/// <summary>
/// Фабрика конвейера инструмента: собирает <see cref="FractalPipeline"/> из
/// настроек инструмента и общего канала записи. Инкапсулирует создание
/// <c>TickService</c> и пайплайна (DIP: потребители не зависят от конкретики).
/// При создании инициализирует базу открытого интереса дня из БД.
/// </summary>
public interface IInstrumentPipelineFactory
{
    /// <summary>
    /// Создать пайплайн для инструмента.
    /// </summary>
    /// <param name="symbolInfo">Информация о символе.</param>
    /// <param name="writerChannel">Общий канал записи фракталов.</param>
    /// <param name="symbolName">Постоянное имя инструмента (для имени логгера).</param>
    /// <param name="cancellationToken">Токен отмены (для запроса базы интереса дня).</param>
    Task<FractalPipeline> CreateAsync(
        SymbolInfo symbolInfo,
        ChannelWriter<FractalRecord> writerChannel,
        string symbolName,
        CancellationToken cancellationToken = default);
}
