using ArtasCore.Abstractions;
using ArtasCore.Models;
using Microsoft.Extensions.Logging;
using Stat.Application.Services;

namespace Stat.Infrastructure.Services;

/// <summary>
/// Сервис загрузки фракталов из БД и уведомления подписчиков через callback.
/// Прямой перенос Python <c>artas2026.services.fractal_from_db.FractalServiceFromDb</c>
/// (в объёме, необходимом stat-сервису).
/// </summary>
public sealed class FractalServiceFromDb : FractalBufferService
{
    private readonly IFractalReader _reader;
    private readonly IStatRepository _statRepository;
    private readonly ILogger<FractalServiceFromDb> _logger;

    /// <summary>Идентификатор символа из таблицы <c>symbols</c>.</summary>
    public int SymbolId { get; }

    /// <summary>Знаков после запятой (загружается из БД).</summary>
    public int Digits { get; }

    /// <summary>Шаг цены (загружается из БД).</summary>
    public double Point { get; }

    /// <summary>
    /// Создать сервис для заданного постоянного имени символа.
    /// </summary>
    /// <param name="name">Постоянное имя символа (например, "CNYRUBF").</param>
    /// <param name="reader">Репозиторий чтения фракталов.</param>
    /// <param name="statRepository">Репозиторий записи статистики.</param>
    /// <param name="logger">Логгер.</param>
    /// <param name="bufferSize">Размер циклического буфера.</param>
    public FractalServiceFromDb(
        string name,
        IFractalReader reader,
        IStatRepository statRepository,
        ILogger<FractalServiceFromDb> logger,
        int bufferSize = 500)
        : base(bufferSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(statRepository);
        ArgumentNullException.ThrowIfNull(logger);

        Name = name;
        _reader = reader;
        _statRepository = statRepository;
        _logger = logger;

        var info = _reader.GetSymbolInfoAsync(name).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException($"Символ '{name}' не найден в таблице symbols.");

        SymbolId = info.Id;
        Digits = info.Digits;
        Point = info.Point;

        _logger.LogInformation("Символ {Name}: digits={Digits}, point={Point}", Name, Digits, Point);
    }

    /// <summary>Постоянное имя символа.</summary>
    public string Name { get; }

    /// <summary>
    /// Загрузить фракталы, начиная с последнего загруженного ID.
    /// Аналог Python <c>fractals_from_last()</c>.
    /// </summary>
    /// <param name="limit">Максимум записей.</param>
    /// <param name="hasStat">Фильтр по наличию статистики.</param>
    /// <returns>Количество загруженных записей.</returns>
    public async Task<int> FractalsFromLastAsync(
        int limit = 500,
        bool? hasStat = null,
        CancellationToken cancellationToken = default)
    {
        var records = await _reader.GetFractalsAsync(
            Name,
            limit: limit,
            fractalId: LastId == 0 ? null : LastId,
            hasStat: hasStat,
            cancellationToken: cancellationToken);

        foreach (var fractal in records)
        {
            AddFractal(fractal);
        }

        return records.Count;
    }

    /// <summary>Очистить статистику по всем фракталам символа.</summary>
    public Task ClearStatAsync(CancellationToken cancellationToken = default) =>
        _statRepository.ClearStatAsync(SymbolId, cancellationToken);

    /// <summary>Обновить статистику одного фрактала.</summary>
    public Task<bool> UpdateStatAsync(
        long fractalId, StatF stat, CancellationToken cancellationToken = default) =>
        _statRepository.UpdateStatAsync(SymbolId, fractalId, stat, cancellationToken);
}
