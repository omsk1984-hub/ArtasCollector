using Microsoft.Extensions.Logging;
using Stat.Application.Abstractions;

namespace Stat.Worker;

/// <summary>
/// Раннер batch-прогона: запускает <see cref="IStatWalker"/> и логирует результат.
/// </summary>
public sealed class StatRunner
{
    private readonly IStatWalker _walker;
    private readonly ILogger<StatRunner> _logger;

    public StatRunner(IStatWalker walker, ILogger<StatRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(walker);
        ArgumentNullException.ThrowIfNull(logger);

        _walker = walker;
        _logger = logger;
    }

    /// <summary>
    /// Выполнить прогон и вернуть exit code (0 — успех, 1 — ошибка).
    /// </summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var saved = await _walker.RunAsync(cancellationToken);
            _logger.LogInformation("Прогон завершён. Сохранено записей: {Saved}", saved);
            return 0;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Прогон отменён.");
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка прогона статистики.");
            return 1;
        }
    }
}
