using System.Diagnostics;
using ArtasCore.Abstractions;
using ArtasCore.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stat.Application;
using Stat.Application.Abstractions;
using Stat.Application.Buffers;
using Stat.Application.Options;

namespace Stat.Infrastructure.Services;

/// <summary>
/// Batch-обработчик статистики: читает фракталы из БД порциями, прогоняет
/// <see cref="StatCursor"/> по каждому и записывает результат обратно.
/// Прямой перенос Python <c>artas2026.services.stat_service.StatService</c>.
/// </summary>
public sealed class StatWalker : IStatWalker
{
    private readonly StatOptions _options;
    private readonly IFractalReader _reader;
    private readonly IStatRepository _statRepository;
    private readonly ILogger<StatWalker> _logger;

    private FractalServiceFromDb? _service;
    private StatCursor? _cursor;

    private int _currentFIndex;
    private long _savedCount;

    public StatWalker(
        IOptions<StatOptions> options,
        IFractalReader reader,
        IStatRepository statRepository,
        ILogger<StatWalker> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(statRepository);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _reader = reader;
        _statRepository = statRepository;
        _logger = logger;
    }

    /// <summary>Количество сохранённых записей статистики.</summary>
    public long SavedCount => _savedCount;

    /// <inheritdoc />
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        Initialize();

        _logger.LogInformation("{Sep}", new string('=', 50));
        _logger.LogInformation("Конфигурация StatWalker:");
        _logger.LogInformation("  Symbol: {Symbol}", _options.Symbol);
        _logger.LogInformation("  MaxDeep: {MaxDeep}", _options.MaxDeep);
        _logger.LogInformation("  stop_point: {StopPoint}", _options.StopPoint);
        _logger.LogInformation("  take_point: {TakePoint}", _options.TakePoint);
        _logger.LogInformation("  digits: {Digits}, point: {Point}", _cursor!.Digits, _cursor.Point);
        _logger.LogInformation("{Sep}", new string('=', 50));

        if (_options.Clear)
        {
            await _service!.ClearStatAsync(cancellationToken);
            _logger.LogInformation("Статистика очищена (clear=true).");
        }

        var total = 0;
        var count = 1;
        while (count > 0)
        {
            var sw = Stopwatch.StartNew();
            count = await _service!.FractalsFromLastAsync(
                limit: _options.ReadChunkSize, hasStat: false, cancellationToken: cancellationToken);
            sw.Stop();

            total += count;
            _logger.LogInformation("{Saved}/{Total} за {Elapsed} мс.", _savedCount, total, sw.ElapsedMilliseconds);
        }

        _logger.LogInformation("Сохранено {Saved}/{Total}", _savedCount, total);
        return (int)_savedCount;
    }

    /// <summary>Инициализировать сервис БД и курсор значениями из БД.</summary>
    private void Initialize()
    {
        _service = new FractalServiceFromDb(
            _options.Symbol, _reader, _statRepository,
            NullLogger<FractalServiceFromDb>.Instance,
            _options.BufferSize);

        _cursor = new StatCursor(
            symbol: _options.Symbol,
            stopPoint: _options.StopPoint,
            takePoint: _options.TakePoint)
        {
            Point = _service.Point,
            Digits = _service.Digits,
        };

        _service.AddFractalCallback(OnFractal);
        _currentFIndex = 0;
        _savedCount = 0;
    }

    /// <summary>
    /// Callback при добавлении нового фрактала. Аналог Python <c>_on_fractal()</c>.
    /// </summary>
    private void OnFractal(Fractal fractal, CircularBuffer<Fractal> fractals)
    {
        var endIndex = _currentFIndex + _options.MaxDeep;
        var count = fractals.Count;
        if (count < endIndex)
        {
            return;
        }

        var current = fractals.GetByIndex(_currentFIndex);
        if (current is null)
        {
            return;
        }

        var index = 1;
        var stat = _cursor!.Init(current.Price, !current.IsUp);

        for (var i = _currentFIndex + 1; i < endIndex; i++)
        {
            var frc = fractals.GetByIndex(i);
            if (frc is not null)
            {
                var (updated, isStopped) = _cursor.Check(stat, frc, index);
                stat = updated;
                if (isStopped)
                {
                    break;
                }
            }
            else
            {
                _logger.LogError("Фрактал i={I} не загружен", i);
            }

            index++;
        }

        current.Stat = stat;
        SaveStatisticsToDb(current);
        _currentFIndex++;
    }

    /// <summary>
    /// Сохранить статистику фрактала в БД. Аналог Python <c>save_statistics_to_db()</c>.
    /// </summary>
    private void SaveStatisticsToDb(Fractal fractal)
    {
        try
        {
            var success = _statRepository
                .UpdateStatAsync(_service!.SymbolId, fractal.Id, fractal.Stat)
                .GetAwaiter().GetResult();

            if (success)
            {
                _savedCount++;
            }
            else
            {
                _logger.LogError("Не удалось сохранить stat для фрактала {Id}", fractal.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка сохранения stat для фрактала {Id}", fractal.Id);
        }
    }
}
