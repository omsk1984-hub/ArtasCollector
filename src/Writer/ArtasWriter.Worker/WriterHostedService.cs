using ArtasWriter.Application.Options;
using ArtasCore.Abstractions;
using ArtasWriter.Infrastructure.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArtasWriter.Worker;

/// <summary>
/// Hosted service: управляет жизненным циклом writer-конвейера. Детали
/// оркестрации делегированы <see cref="PipelineOrchestrator"/>. Оставляет за собой
/// только стартовую проверку БД, ожидание остановки и логирование итога (SRP).
/// </summary>
public sealed class WriterHostedService : BackgroundService
{
    private readonly PipelineOrchestrator _orchestrator;
    private readonly IFractalWriter _writer;
    private readonly IHealthProbe _healthProbe;
    private readonly ISingleInstanceGuard _guard;
    private readonly IWriterState _state;
    private readonly TimeSpan _shutdownTimeout;
    private readonly ILogger<WriterHostedService> _logger;

    public WriterHostedService(
        PipelineOrchestrator orchestrator,
        IFractalWriter writer,
        IHealthProbe healthProbe,
        ISingleInstanceGuard guard,
        IWriterState state,
        IOptions<WriterOptions> options,
        ILogger<WriterHostedService> logger)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _healthProbe = healthProbe ?? throw new ArgumentNullException(nameof(healthProbe));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var opts = options?.Value ?? new WriterOptions();
        _shutdownTimeout = TimeSpan.FromSeconds(15);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Запуск Writer, старт: {Start}", _state.StartTimeUtc);

        // Защита от второго экземпляра: без эксклюзивной блокировки запись
        // не стартует. Иначе параллельный процесс своим первым фракталом
        // задаст базу интереса дня = своему интересу (from_start_interest = 0).
        var lockAcquired = await _guard.TryAcquireAsync(stoppingToken);
        if (!lockAcquired)
        {
            _logger.LogCritical(
                "Другой экземпляр Writer уже работает (блокировка занята). " +
                "Конвейеры записи не запускаются — завершаю процесс.");
            throw new InvalidOperationException(
                "Второй экземпляр Writer запрещён: не удалось получить блокировку.");
        }

        _state.SetDbConnected(
            await _healthProbe.TestConnectionAsync(stoppingToken));
        if (!_state.DbConnected)
        {
            _logger.LogWarning("PostgreSQL недоступен на старте — запись будет повторяться");
        }

        await _orchestrator.StartAsync(stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Остановка Writer...");
        }
        finally
        {
            await _orchestrator.StopAsync();

            _logger.LogInformation(
                "Writer остановлен. Всего записано фракталов: {Total}", _writer.TotalWritten);

            // Освобождаем блокировку единственного экземпляра.
            await _guard.DisposeAsync();
        }
    }
}
