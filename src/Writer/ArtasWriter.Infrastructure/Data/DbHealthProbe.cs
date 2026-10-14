using ArtasCore.Abstractions;
using Npgsql;

namespace ArtasCore.Data;

/// <summary>
/// Проверка подключения к PostgreSQL.
/// </summary>
public sealed class DbHealthProbe : IHealthProbe
{
    private readonly DbConfig _config;

    public DbHealthProbe(DbConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <inheritdoc />
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_config.ConnectionString);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand("SELECT 1", conn);
            await cmd.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
