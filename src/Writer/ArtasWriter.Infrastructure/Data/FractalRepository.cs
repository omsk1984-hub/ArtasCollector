using System.Text.Json;
using ArtasCore.Abstractions;
using ArtasCore.Models;
using Npgsql;
using NpgsqlTypes;

namespace ArtasCore.Data;

/// <summary>
/// Запись фракталов в <c>public.fractals</c> с батчингом и идемпотентностью.
/// Аналог Python <c>FractalWriterService.save_fractal()</c>, но с батчем через unnest.
/// </summary>
public sealed class FractalRepository : IFractalRepository
{
    /// <summary>
    /// Значение-маркер колонки <c>stat</c>, которым помечаются записи, созданные
    /// в режиме симуляции. Позволяет однозначно находить и удалять тестовые
    /// данные: <c>DELETE FROM public.fractals WHERE stat = '"Fake"'::jsonb;</c>.
    /// </summary>
    public const string SimulationStatMarker = "Fake";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly DbConfig _config;
    private readonly bool _simulationMode;

    /// <param name="config">Конфигурация подключения к PostgreSQL.</param>
    /// <param name="simulationMode">
    /// true — включён режим симуляции: все записываемые фракталы получат
    /// в колонке <c>stat</c> маркер <see cref="SimulationStatMarker"/>.
    /// </param>
    public FractalRepository(DbConfig config, bool simulationMode = false)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _simulationMode = simulationMode;
    }

    /// <inheritdoc />
    public async Task<bool> SaveAsync(
        int symbolId, Fractal fractal, CancellationToken cancellationToken = default)
    {
        var inserted = await SaveBatchAsync(new[] { (symbolId, fractal) }, cancellationToken);
        return inserted > 0;
    }

    /// <inheritdoc />
    public async Task<int> SaveBatchAsync(
        IReadOnlyList<(int SymbolId, Fractal Fractal)> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
        {
            return 0;
        }

        // Параллельные массивы для unnest.
        var ids = new long[records.Count];
        var symbolIds = new int[records.Count];
        var prices = new double[records.Count];
        var times = new DateTime[records.Count];
        var metadata = new string[records.Count];

        for (var i = 0; i < records.Count; i++)
        {
            var (symbolId, fractal) = records[i];
            ids[i] = fractal.Id;
            symbolIds[i] = symbolId;
            prices[i] = fractal.Price;
            // ВАЖНО: колонка time хранится в UTC. Пишем время пика по UTC
            // (TickPeak.TimeUtc), а не МСК — иначе границы дня в SQL
            // (CountTodayAsync/GetDayStartInterestAsync) расходятся с логикой.
            times[i] = fractal.TickPeak.TimeUtc;
            metadata[i] = JsonSerializer.Serialize(FractalMetadataDto.FromFractal(fractal), JsonOptions);
        }

        // В режиме симуляции помечаем запись в stat маркером "Fake",
        // чтобы тестовые данные можно было однозначно найти и удалить.
        // Значение — валидный JSONB-литерал (JSON-строка в двойных кавычках).
        var statValue = _simulationMode
            ? $"'\"{SimulationStatMarker}\"'::jsonb"
            : "NULL";

        var sql = $"""
            INSERT INTO public.fractals (id, symbol_id, price, time, metadata, stat)
            SELECT id, symbol_id, price, time, metadata::jsonb, {statValue}
            FROM unnest(@ids, @symbol_ids, @prices, @times, @metadata)
                 AS t(id, symbol_id, price, time, metadata)
            ON CONFLICT (id, symbol_id) DO NOTHING
            """;

        await using var conn = new NpgsqlConnection(_config.ConnectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);

        cmd.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = ids });
        cmd.Parameters.Add(new NpgsqlParameter("symbol_ids", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = symbolIds });
        cmd.Parameters.Add(new NpgsqlParameter("prices", NpgsqlDbType.Array | NpgsqlDbType.Double) { Value = prices });
        cmd.Parameters.Add(new NpgsqlParameter("times", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = times });
        cmd.Parameters.Add(new NpgsqlParameter("metadata", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = metadata });

        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> CountTodayAsync(int symbolId, CancellationToken cancellationToken = default)
    {
        // ВАЖНО: границы дня считаем по UTC (а не по МСК) — вся логика проекта
        // ведётся в UTC, МСК используется только для визуализации. Граница дня
        // должна совпадать с UTC-датой хранения (public.fractals.time), иначе
        // первый фрактал нового календарного дня получит базу от предыдущего дня.
        var startOfDayUtc = DateTime.UtcNow.Date;

        const string sql = """
            SELECT COUNT(*) FROM public.fractals
            WHERE symbol_id = @symbol_id AND time >= @start AND deleted = false
            """;

        await using var conn = new NpgsqlConnection(_config.ConnectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("symbol_id", symbolId);
        cmd.Parameters.AddWithValue("start", startOfDayUtc);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is long count ? (int)count : 0;
    }

    /// <inheritdoc />
    public async Task<long?> GetDayStartInterestAsync(
        int symbolId, CancellationToken cancellationToken = default)
    {
        // Начало текущего дня по UTC (тот же расчёт, что в CountTodayAsync).
        // Вся логика — в UTC, МСК только для визуализации.
        var startOfDayUtc = DateTime.UtcNow.Date;

        // Интерес берём из metadata->'peak'->>'interest' самого раннего
        // боевого фрактала дня. Симуляционные записи (stat = '"Fake"'::jsonb)
        // исключаем; записи с реальной статистикой Stat (stat заполнен иным
        // значением) учитываются наравне с stat IS NULL.
        const string sql = """
            SELECT metadata->'peak'->>'interest'
            FROM public.fractals
            WHERE symbol_id = @symbol_id
              AND time >= @start
              AND deleted = false
              AND stat IS DISTINCT FROM '"Fake"'::jsonb
            ORDER BY time ASC
            LIMIT 1
            """;

        await using var conn = new NpgsqlConnection(_config.ConnectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("symbol_id", symbolId);
        cmd.Parameters.AddWithValue("start", startOfDayUtc);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        if (result is null or DBNull)
        {
            return null;
        }

        // Значение хранится как JSON-строка/число; у писателя это строка.
        return result switch
        {
            long l => l,
            int i => i,
            decimal d => (long)d,
            string s when long.TryParse(s, out var parsed) => parsed,
            _ => null,
        };
    }
}
