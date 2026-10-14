using ArtasCore.Data;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace ArtasWriter.Tests.Infrastructure;

/// <summary>
/// Интеграционный тест реальных данных Writer: проверяет корректность
/// <c>metadata.from_start_interest</c> для символа <c>USDRUBF</c> (symbol_id = 3)
/// за ТЕКУЩИЙ календарный день (UTC).
///
/// ВАЖНО: вся логика ведётся по UTC, МСК (UTC+3) — только для визуализации.
/// Границы дня определяются по UTC-дате хранения (<c>public.fractals.time</c>).
///
/// Правило проекта (см. <c>db/recalculate-from-start-interest.sql</c> и
/// <see cref="ArtasWriter.Application.Services.FractalBufferService.AddFractal"/>):
/// <list type="bullet">
///   <item>база дня = интерес (<c>metadata->'peak'->>'interest'</c>) самого раннего
///   боевого фрактала дня (<c>stat IS NULL</c>, <c>deleted = false</c>);</item>
///   <item><c>from_start_interest = interest фрактала − база дня</c>.</item>
/// </list>
///
/// Тест работает на живой БД (как продакшн-сервис) и НЕ пропускается при
/// недоступности подключения — расхождение считается ошибкой. По умолчанию
/// проверяется <c>USDRUBF</c> (symbol_id = 3); при необходимости символ можно
/// переопределить переменной окружения <c>ARTAS_TEST_SYMBOL_ID</c>.
///
/// Запуск: <c>dotnet test src/Writer/ArtasWriter.slnx --filter UsdrubfFromStartInterest</c>.
/// </summary>
[Trait("Category", "Integration")]
public class UsdrubfFromStartInterestIntegrationTests
{
    private const int DefaultSymbolId = 3; // USDRUBF
    private const string DefaultSymbolName = "USDRUBF";

    private readonly ITestOutputHelper _output;

    public UsdrubfFromStartInterestIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Проверяет, что для каждого боевого фрактала USDRUBF за текущий UTC-день
    /// записанный <c>from_start_interest</c> равен <c>interest − база дня</c>.
    /// База дня — интерес самого раннего боевого фрактала дня.
    /// </summary>
    [Fact]
    public async Task FromStartInterest_Today_MatchesDayBaseCalculation()
    {
        var symbolId = ReadSymbolIdEnv();
        var symbolName = symbolId == DefaultSymbolId ? DefaultSymbolName : $"symbol_id={symbolId}";

        // Начало текущего дня по UTC — ровно тот же расчёт, что в
        // FractalRepository.GetDayStartInterestAsync/CountTodayAsync.
        var startOfDayUtc = DateTime.UtcNow.Date;

        var config = DbConfig.Default;
        var connectionString = config.ConnectionString;

        // Диагностика окружения.
        _output.WriteLine($"БД: {config}");
        _output.WriteLine($"Символ: {symbolName} (symbol_id={symbolId})");
        _output.WriteLine($"Начало UTC-дня: {startOfDayUtc:O}");

        var rows = await LoadRealFractalsTodayAsync(connectionString, symbolId, startOfDayUtc);

        if (rows.Count == 0)
        {
            _output.WriteLine(
                "За текущий UTC-день боевых фракталов в БД нет — проверять нечего. " +
                "Это НЕ ошибка расчёта: Writer, возможно, ещё не записал данные за сегодня.");
            return;
        }

        // База дня = интерес самого раннего боевого фрактала (сортировка по time, id).
        var ordered = rows
            .OrderBy(r => r.TimeUtc)
            .ThenBy(r => r.Id)
            .ToList();

        var baseRow = ordered[0];
        _output.WriteLine(
            $"База дня: id={baseRow.Id}, time={baseRow.TimeUtc:O}, interest={baseRow.Interest}");

        var mismatches = new List<FractalRow>();
        foreach (var row in ordered)
        {
            var expected = row.Interest - baseRow.Interest;
            _output.WriteLine(
                $"id={row.Id} time={row.TimeUtc:O} interest={row.Interest} " +
                $"fsi(записано)={row.FromStartInterest} fsi(ожидаемо)={expected}");

            if (row.FromStartInterest != expected)
            {
                mismatches.Add(row);
            }
        }

        if (mismatches.Count > 0)
        {
            var details = string.Join(
                Environment.NewLine,
                mismatches.Select(m =>
                    $"  id={m.Id} time={m.TimeUtc:O}: записано fsi={m.FromStartInterest}, " +
                    $"ожидалось {m.Interest - baseRow.Interest} (interest={m.Interest}, база={baseRow.Interest})"));

            Assert.Fail(
                $"Найдено {mismatches.Count} расхождений from_start_interest за текущий UTC-день " +
                $"для {symbolName} (symbol_id={symbolId}). База дня = {baseRow.Interest} " +
                $"(фрактал id={baseRow.Id}).{Environment.NewLine}{details}");
        }

        _output.WriteLine(
            $"OK: все {ordered.Count} боевых фракталов за текущий UTC-день имеют " +
            $"корректный from_start_interest (база дня = {baseRow.Interest}).");
    }

    private static int ReadSymbolIdEnv()
    {
        var raw = Environment.GetEnvironmentVariable("ARTAS_TEST_SYMBOL_ID");
        return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : DefaultSymbolId;
    }

    /// <summary>
    /// Прочитать боевые фракталы символа за текущий день. Исключаются
    /// симуляционные записи (<c>stat = '"Fake"'::jsonb</c>) и soft-deleted;
    /// записи с реальной статистикой Stat учитываются.
    /// </summary>
    private static async Task<List<FractalRow>> LoadRealFractalsTodayAsync(
        string connectionString, int symbolId, DateTime startOfDayUtc)
    {
        const string sql = """
            SELECT
                id,
                time,
                (metadata->'peak'->>'interest')::bigint AS interest,
                (metadata->>'from_start_interest')::bigint AS from_start_interest
            FROM public.fractals
            WHERE symbol_id = @symbol_id
              AND time >= @start
              AND deleted = false
              AND stat IS DISTINCT FROM '"Fake"'::jsonb
              AND metadata->'peak'->>'interest' IS NOT NULL
              AND metadata->>'from_start_interest' IS NOT NULL
            ORDER BY time ASC, id ASC
            """;

        var result = new List<FractalRow>();

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("symbol_id", symbolId);
        cmd.Parameters.AddWithValue("start", startOfDayUtc);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new FractalRow(
                Id: reader.GetInt64(0),
                TimeUtc: reader.GetFieldValue<DateTime>(1),
                Interest: reader.GetInt64(2),
                FromStartInterest: reader.GetInt64(3)));
        }

        return result;
    }

    private readonly record struct FractalRow(
        long Id,
        DateTime TimeUtc,
        long Interest,
        long FromStartInterest);
}
