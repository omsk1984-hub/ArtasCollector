using System.Text.Json;
using ArtasCore.Data;
using Npgsql;

namespace ArtasChart.Data;

/// <summary>
/// Репозиторий настроек фильтра фракталов по символам
/// (таблица <c>public.symbol_filter_settings</c>) на сыром Npgsql.
/// Настройки сериализуются целиком в одно JSONB-поле <c>settings</c>.
/// Реализует <see cref="ISymbolFilterSettingsStore"/>.
/// </summary>
public sealed class SymbolFilterSettingsRepository : ISymbolFilterSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly DbConfig _config;

    public SymbolFilterSettingsRepository(DbConfig config)
    {
        _config = config;
    }

    /// <inheritdoc />
    public async Task<FractalFilterSettings?> LoadAsync(
        string name, CancellationToken cancellationToken = default)
    {
        // Символ ищется по постоянному имени; при отсутствии символа или настроек — null.
        const string sql = """
            SELECT sfs.settings::text
            FROM public.symbol_filter_settings sfs
            JOIN public.symbols s ON s.id = sfs.symbol_id
            WHERE s.name = @name
            LIMIT 1
            """;

        await using var conn = new NpgsqlConnection(_config.ConnectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("name", name);

        var raw = await cmd.ExecuteScalarAsync(cancellationToken);
        if (raw is not string settingsRaw || string.IsNullOrEmpty(settingsRaw))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<FractalFilterSettings>(settingsRaw, JsonOptions);
        }
        catch (JsonException)
        {
            // Повреждённый JSON — считаем, что настроек нет.
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        string name,
        FractalFilterSettings settings,
        CancellationToken cancellationToken = default)
    {
        // Upsert по уникальному symbol_id: одна строка настроек на символ.
        // Символ должен существовать (FK на symbols.id); иначе — ошибка.
        const string sql = """
            INSERT INTO public.symbol_filter_settings (symbol_id, settings)
            SELECT s.id, @settings::jsonb
            FROM public.symbols s
            WHERE s.name = @name
            ON CONFLICT (symbol_id) DO UPDATE
                SET settings = EXCLUDED.settings,
                    updated_at = CURRENT_TIMESTAMP
            """;

        var json = JsonSerializer.Serialize(settings, JsonOptions);

        await using var conn = new NpgsqlConnection(_config.ConnectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("settings", json);

        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
        {
            throw new InvalidOperationException(
                $"Не удалось сохранить настройки фильтра: символ '{name}' не найден в таблице symbols");
        }
    }
}
