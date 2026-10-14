using System.Text.Json;
using ArtasCore.Abstractions;
using ArtasCore.Data;
using ArtasCore.Models;
using Npgsql;
using NpgsqlTypes;

namespace Stat.Infrastructure.Data;

/// <summary>
/// Запись статистики фракталов в <c>public.fractals.stat</c> (JSONB).
/// Аналог Python-методов <c>FractalDbService.update_fractal_stat()</c> и
/// <c>FractalDbService.clear_stat()</c>, с батчингом через unnest.
/// </summary>
public sealed class StatRepository : IStatRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly DbConfig _config;

    public StatRepository(DbConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
    }

    /// <inheritdoc />
    public async Task ClearStatAsync(int symbolId, CancellationToken cancellationToken = default)
    {
        const string sql = "UPDATE public.fractals SET stat = NULL WHERE symbol_id = @symbol_id";

        await using var conn = new NpgsqlConnection(_config.ConnectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("symbol_id", symbolId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateStatAsync(
        int symbolId,
        long fractalId,
        StatF stat,
        CancellationToken cancellationToken = default)
    {
        var updated = await UpdateStatBatchAsync(
            symbolId, new[] { (fractalId, stat) }, cancellationToken);
        return updated > 0;
    }

    /// <inheritdoc />
    public async Task<int> UpdateStatBatchAsync(
        int symbolId,
        IReadOnlyList<(long FractalId, StatF Stat)> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
        {
            return 0;
        }

        var ids = new long[records.Count];
        var stats = new string[records.Count];

        for (var i = 0; i < records.Count; i++)
        {
            ids[i] = records[i].FractalId;
            stats[i] = JsonSerializer.Serialize(records[i].Stat, JsonOptions);
        }

        const string sql = """
            UPDATE public.fractals AS f
            SET stat = t.stat::jsonb
            FROM unnest(@ids, @stats) AS t(id, stat)
            WHERE f.id = t.id AND f.symbol_id = @symbol_id
            """;

        await using var conn = new NpgsqlConnection(_config.ConnectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);

        cmd.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = ids });
        cmd.Parameters.Add(new NpgsqlParameter("stats", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = stats });
        cmd.Parameters.AddWithValue("symbol_id", symbolId);

        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
