using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ArtasWriter.Application.Options;
using ArtasWriter.Application.Pipeline;
using ArtasWriter.Application.Services;
using ArtasCore.Abstractions;
using ArtasCore.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArtasWriter.Application.Producers;

/// <summary>
/// Реализация <see cref="IInstrumentPipelineFactory"/>: создаёт <see cref="TickService"/>
/// и <see cref="FractalPipeline"/> для инструмента. Централизует создание сервисов
/// инструмента (ранее — внутри <c>FinamProducerFactory</c>).
/// При создании инициализирует базу открытого интереса дня из БД: если за текущий
/// день есть боевые фракталы, за базу берётся интерес самого раннего из них — тогда
/// <c>FromStartInterest</c> считается от начала дня, а не от первого фрактала,
/// полученного после рестарта Writer. Если фракталов дня нет — база возьмётся
/// из первого полученного фрактала (штатное поведение <see cref="FractalBufferService"/>).
/// </summary>
public sealed class InstrumentPipelineFactory : IInstrumentPipelineFactory
{
    private readonly WriterOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IFractalRepository _fractalRepository;

    public InstrumentPipelineFactory(
        IOptions<WriterOptions> options,
        ILoggerFactory loggerFactory,
        IFractalRepository fractalRepository)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _fractalRepository = fractalRepository ?? throw new ArgumentNullException(nameof(fractalRepository));
    }

    /// <inheritdoc />
    public async Task<FractalPipeline> CreateAsync(
        SymbolInfo symbolInfo,
        ChannelWriter<FractalRecord> writerChannel,
        string symbolName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(symbolInfo);
        ArgumentNullException.ThrowIfNull(writerChannel);

        var logger = _loggerFactory.CreateLogger($"FractalPipeline.{symbolName}");
        var tickService = new TickService(symbolInfo, _options.FractalBufferSize);

        // База интереса на начало текущего дня (UTC) из БД. null — фракталов дня нет.
        // День считаем по UTC (как FractalRepository.CountTodayAsync / GetDayStartInterestAsync):
        // вся логика — в UTC, МСК используется только для визуализации.
        try
        {
            var dayStartInterest = await _fractalRepository
                .GetDayStartInterestAsync(symbolInfo.Id, cancellationToken);

            if (dayStartInterest.HasValue)
            {
                tickService.SetDayStartInterest(CurrentUtcDay(), dayStartInterest.Value);
                logger.LogInformation(
                    "База интереса дня из БД для {Symbol}: {Interest}",
                    symbolName, dayStartInterest.Value);
            }
            else
            {
                logger.LogInformation(
                    "Фракталов за текущий день в БД нет для {Symbol} — база будет взята из первого фрактала",
                    symbolName);
            }
        }
        catch (Exception ex)
        {
            // Не блокируем запуск инструмента из-за ошибки чтения: база возьмётся
            // из первого полученного фрактала (штатное поведение).
            logger.LogWarning(ex,
                "Не удалось получить базу интереса дня для {Symbol} — используется fallback",
                symbolName);
        }

        return new FractalPipeline(
            tickService,
            symbolInfo.Id,
            writerChannel,
            logger,
            symbolName);
    }

    /// <summary>
    /// Текущая календарная дата по UTC. Вся логика проекта ведётся в UTC,
    /// МСК (UTC+3) используется только для визуализации.
    /// </summary>
    private static DateOnly CurrentUtcDay() => DateOnly.FromDateTime(DateTime.UtcNow);
}
