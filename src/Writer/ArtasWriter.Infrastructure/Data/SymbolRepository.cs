using ArtasCore.Abstractions;
using ArtasCore.Models;
using Npgsql;

namespace ArtasCore.Data;

/// <summary>
/// Чтение информации о символах из <c>public.symbols</c>.
/// </summary>
public sealed class SymbolRepository : ISymbolRepository
{
    private readonly DbConfig _config;

    public SymbolRepository(DbConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <inheritdoc />
    public Task<SymbolInfo?> GetBySymbolAsync(string symbol, CancellationToken cancellationToken = default)
    {
        // symbol — код контракта (например, "SiZ6").
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        return QueryAsync("symbol = @value", symbol, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SymbolInfo?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        // name — постоянный ключ инструмента (например, "Si"), используется в appsettings.
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return QueryAsync("name = @value", name, cancellationToken);
    }

    private async Task<SymbolInfo?> QueryAsync(
        string whereClause, string value, CancellationToken cancellationToken)
    {
        // symbol — ключ из appsettings; name — код контракта Finam.
        var sql =
            "SELECT id, COALESCE(name, ''), COALESCE(symbol, ''), COALESCE(description, ''), " +
            "COALESCE(digits, 3), COALESCE(point, 0.001) " +
            $"FROM public.symbols WHERE {whereClause} LIMIT 1";

        await using var conn = new NpgsqlConnection(_config.ConnectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("value", value);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SymbolInfo
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Symbol = reader.GetString(2),
            Description = reader.GetString(3),
            Digits = reader.GetInt32(4),
            Point = reader.GetDouble(5),
        };
    }
}
