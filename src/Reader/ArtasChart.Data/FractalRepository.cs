using System.Text.Json;
using ArtasCore.Abstractions;
using ArtasCore.Data;
using ArtasCore.Models;
using Npgsql;

namespace ArtasChart.Data;

/// <summary>
/// Репозиторий чтения фракталов из PostgreSQL.
/// Аналог Python <c>FractalReaderService</c> / <c>FractalDbService</c>.
/// Реализует сегрегированные интерфейсы <see cref="IFractalReader"/>,
/// <see cref="IHealthProbe"/> и <see cref="IFractalCounter"/>.
/// </summary>
public sealed class FractalRepository : IFractalReader, IHealthProbe, IFractalCounter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly DbConfig _config;

    public FractalRepository(DbConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Получить информацию о символе по постоянному имени (таблица symbols).
    /// </summary>
    public async Task<SymbolInfo?> GetSymbolInfoAsync(
        string name, CancellationToken cancellationToken = default)
    {
        const string sql =
            "SELECT id, name, symbol, description, digits, point " +
            "FROM symbols WHERE name = @name LIMIT 1";

        await using var conn = new NpgsqlConnection(_config.ConnectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("name", name);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SymbolInfo
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Symbol = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
            Digits = reader.IsDBNull(4) ? 3 : reader.GetInt32(4),
            Point = reader.IsDBNull(5) ? 0.001 : reader.GetDouble(5),
        };
    }

    /// <summary>
    /// Получить список постоянных имён всех символов (для выпадающего списка),
    /// упорядоченный по имени.
    /// </summary>
    public async Task<List<string>> GetSymbolNamesAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT name FROM symbols ORDER BY name";

        var result = new List<string>();

        await using var conn = new NpgsqlConnection(_config.ConnectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0))
            {
                result.Add(reader.GetString(0));
            }
        }

        return result;
    }

    /// <summary>
    /// Получить фракталы символа. Повторяет логику Python-метода
    /// <c>FractalReaderService.get_fractals()</c>.
    /// </summary>
    /// <param name="name">Постоянное имя символа (например, "Si").</param>
    /// <param name="limit">Максимум записей.</param>
    /// <param name="fractalId">Выбрать записи с id строго больше указанного.</param>
    /// <param name="startTime">Начальное время (включительно).</param>
    /// <param name="endTime">Конечное время (включительно).</param>
    /// <param name="hasStat">Фильтр по наличию stat (true/false/null).</param>
    /// <param name="orderDesc">true — последние записи (результат всё равно ASC).</param>
    public async Task<List<Fractal>> GetFractalsAsync(
        string name,
        int limit = 100,
        long? fractalId = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        bool? hasStat = null,
        bool orderDesc = false,
        CancellationToken cancellationToken = default)
    {
        var symbol = await GetSymbolInfoAsync(name, cancellationToken)
            ?? throw new InvalidOperationException($"Символ '{name}' не найден в таблице symbols");

        var conditions = new List<string> { "f.symbol_id = @symbol_id" };
        if (fractalId.HasValue)
        {
            conditions.Add("f.id > @fractal_id");
        }
        if (startTime.HasValue)
        {
            conditions.Add("f.time >= @start_time");
        }
        if (endTime.HasValue)
        {
            conditions.Add("f.time <= @end_time");
        }
        if (hasStat.HasValue)
        {
            conditions.Add(hasStat.Value ? "f.stat IS NOT NULL" : "f.stat IS NULL");
        }

        var where = " WHERE " + string.Join(" AND ", conditions);

        string query;
        if (orderDesc)
        {
            query =
                "SELECT * FROM (" +
                $"SELECT f.* FROM fractals f{where} ORDER BY id DESC LIMIT @limit OFFSET 0" +
                ") sub ORDER BY id ASC";
        }
        else
        {
            query = $"SELECT f.* FROM fractals f{where} ORDER BY id ASC LIMIT @limit OFFSET 0";
        }

        var result = new List<Fractal>();

        await using var conn = new NpgsqlConnection(_config.ConnectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(query, conn);
        cmd.Parameters.AddWithValue("symbol_id", symbol.Id);
        cmd.Parameters.AddWithValue("limit", limit);
        if (fractalId.HasValue)
        {
            cmd.Parameters.AddWithValue("fractal_id", fractalId.Value);
        }
        if (startTime.HasValue)
        {
            cmd.Parameters.AddWithValue("start_time", startTime.Value);
        }
        if (endTime.HasValue)
        {
            cmd.Parameters.AddWithValue("end_time", endTime.Value);
        }

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var fractal = MapFractal(reader);
            if (fractal is not null)
            {
                result.Add(fractal);
            }
        }

        return result;
    }

    /// <summary>
    /// Сопоставить строку результата с объектом <see cref="Fractal"/>.
    /// Ожидаемые колонки: id, symbol_id, price, time, metadata, stat.
    /// </summary>
    private static Fractal? MapFractal(NpgsqlDataReader reader)
    {
        var id = reader.GetInt64(reader.GetOrdinal("id"));
        var price = reader.IsDBNull(reader.GetOrdinal("price"))
            ? 0.0
            : reader.GetDouble(reader.GetOrdinal("price"));
        var metadataRaw = reader.IsDBNull(reader.GetOrdinal("metadata"))
            ? null
            : reader.GetString(reader.GetOrdinal("metadata"));
        var statRaw = reader.IsDBNull(reader.GetOrdinal("stat"))
            ? null
            : reader.GetString(reader.GetOrdinal("stat"));

        FractalMetadataDto? metadata = null;
        if (!string.IsNullOrEmpty(metadataRaw))
        {
            try
            {
                metadata = JsonSerializer.Deserialize<FractalMetadataDto>(metadataRaw, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        if (metadata?.Info is null || metadata.Peak is null)
        {
            return null;
        }

        var fractal = new Fractal
        {
            Id = id,
            Price = price,
            IsUp = metadata.IsUp,
            Distance = metadata.Distance,
            Min = metadata.Min,
            Max = metadata.Max,
            DefInterest = metadata.DefInterest,
            FromStartInterest = metadata.FromStartInterest,
            TickPeak = metadata.Peak,
            Info = metadata.Info,
        };

        if (!string.IsNullOrEmpty(statRaw))
        {
            try
            {
                var stat = JsonSerializer.Deserialize<StatF>(statRaw, JsonOptions);
                if (stat is not null)
                {
                    fractal.Stat = stat;
                }
            }
            catch (JsonException)
            {
                // stat повреждён — оставляем дефолтный StatF
            }
        }

        return fractal;
    }

    /// <summary>
    /// Проверить подключение к БД (SELECT 1).
    /// </summary>
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

    /// <summary>
    /// Посчитать фракталы символа (для статус-строки).
    /// </summary>
    public async Task<long> CountFractalsAsync(
        string name, CancellationToken cancellationToken = default)
    {
        var symbol = await GetSymbolInfoAsync(name, cancellationToken);
        if (symbol is null)
        {
            return -1;
        }

        await using var conn = new NpgsqlConnection(_config.ConnectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM fractals WHERE symbol_id = @symbol_id", conn);
        cmd.Parameters.AddWithValue("symbol_id", symbol.Id);

        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return value is long count ? count : 0;
    }
}
