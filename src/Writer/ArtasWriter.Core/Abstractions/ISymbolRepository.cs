using ArtasCore.Models;

namespace ArtasCore.Abstractions;

/// <summary>
/// Чтение информации о символах из <c>public.symbols</c>.
/// </summary>
public interface ISymbolRepository
{
    /// <summary>
    /// Получить информацию о символе по его коду (колонка <c>symbol</c>).
    /// </summary>
    Task<SymbolInfo?> GetBySymbolAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить информацию о символе по постоянному имени (колонка <c>name</c>).
    /// </summary>
    Task<SymbolInfo?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
}
