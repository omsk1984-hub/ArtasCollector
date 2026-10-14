using ArtasWriter.Application.Producers;
using ArtasCore.Abstractions;
using ArtasCore.Models;

namespace ArtasWriter.FinamApi.Simulation;

/// <summary>
/// Генерирует синтетические события Finam (стакан, открытый интерес, котировки)
/// для сквозной проверки конвейера без реального подключения.
/// </summary>
public sealed class SimulationEventSource : ISimulatedEventSource
{
    /// <summary>Глубина генерируемого синтетического стакана (уровней на сторону).</summary>
    private const int SimulatedDepth = 5;

    /// <inheritdoc />
    public async Task RunAsync(
        string finamSymbol,
        Func<FinamEvent, bool> publishAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(publishAsync);

        var rnd = new Random(finamSymbol.GetHashCode());
        var price = 100.0 + rnd.NextDouble() * 10;
        var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        while (!cancellationToken.IsCancellationRequested)
        {
            // Свежий стакан.
            var bids = new List<(double, int)>(SimulatedDepth);
            var asks = new List<(double, int)>(SimulatedDepth);
            for (var i = 0; i < SimulatedDepth; i++)
            {
                bids.Add((Math.Round(price - 0.001 * (i + 1), 3), rnd.Next(1, 50)));
                asks.Add((Math.Round(price + 0.001 * (i + 1), 3), rnd.Next(1, 50)));
            }
            publishAsync(new FinamEvent(new OrderBookEvent(finamSymbol, bids, asks, time)));
            publishAsync(new FinamEvent(new OpenInterestEvent(finamSymbol, rnd.Next(1000, 5000), time)));

            // Движение цены и котировка.
            price = Math.Round(price + (rnd.NextDouble() - 0.5) * 0.02, 3);
            var flags = rnd.Next(2) == 0 ? TickFlag.Buy : TickFlag.Sell;
            publishAsync(new FinamEvent(
                new QuoteEvent(finamSymbol, price, price + 0.001, price, rnd.Next(1, 10), (int)flags, time)));

            time += 100;
            try
            {
                await Task.Delay(50, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
