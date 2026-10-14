using ArtasCore.Abstractions;
using ArtasChart.Data;

namespace ArtasChart.Services;

/// <summary>
/// Реализация CLI-диагностики: подключение к БД, информация о символе,
/// количество фракталов и выборка последних.
/// </summary>
public sealed class DiagnosticsService : IDiagnosticsService
{
    private readonly IFractalReader _reader;
    private readonly IHealthProbe _health;
    private readonly IFractalCounter _counter;

    public DiagnosticsService(
        IFractalReader reader,
        IHealthProbe health,
        IFractalCounter counter)
    {
        _reader = reader;
        _health = health;
        _counter = counter;
    }

    public async Task CheckDbAsync(string symbol)
    {
        Console.WriteLine($"[check-db] Символ: {symbol}");
        var connected = await _health.TestConnectionAsync();
        Console.WriteLine($"[check-db] Соединение установлено: {connected}");

        if (!connected)
        {
            return;
        }

        var symbolInfo = await _reader.GetSymbolInfoAsync(symbol);
        if (symbolInfo is null)
        {
            Console.WriteLine($"[check-db] Символ '{symbol}' не найден.");
            return;
        }

        var total = await _counter.CountFractalsAsync(symbol);
        Console.WriteLine($"[check-db] {symbolInfo} — фракталов: {total}");

        var sample = await _reader.GetFractalsAsync(symbol, limit: 50, orderDesc: true);
        Console.WriteLine($"[check-db] Загружено последних: {sample.Count}");

        if (sample.Count > 0)
        {
            var last = sample[^1];
            Console.WriteLine(
                $"[check-db] Последний: Id={last.Id} Price={last.Price} IsUp={last.IsUp} " +
                $"Markets={last.Info.Markets.Count} Stat.IsTake={last.Stat.IsTake}");
        }
    }
}