using ArtasCore.Models;

namespace ArtasCore.Abstractions;

/// <summary>
/// Чтение фракталов и информации о символах (сегрегированный интерфейс).
/// </summary>
public interface IFractalReader
{
    /// <summary>
    /// Получить информацию о символе по постоянному имени (таблица symbols).
    /// </summary>
    Task<SymbolInfo?> GetSymbolInfoAsync(
        string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить список постоянных имён всех символов (для выпадающего списка),
    /// упорядоченный по имени.
    /// </summary>
    Task<List<string>> GetSymbolNamesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить фракталы символа.
    /// </summary>
    Task<List<Fractal>> GetFractalsAsync(
        string name,
        int limit = 100,
        long? fractalId = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        bool? hasStat = null,
        bool orderDesc = false,
        CancellationToken cancellationToken = default);
}
