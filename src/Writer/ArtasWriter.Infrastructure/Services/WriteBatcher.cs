using System.Threading.Channels;
using ArtasWriter.Application.Pipeline;

namespace ArtasWriter.Infrastructure.Services;

/// <summary>
/// Формирует батчи из канала: копит элементы до <c>batchSize</c> или до истечения
/// <c>flushInterval</c>, после чего передаёт батч обработчику. Отделяет политику
/// накопления от фактической записи (SRP).
/// </summary>
public sealed class WriteBatcher
{
    private readonly int _batchSize;
    private readonly TimeSpan _flushInterval;
    private readonly Func<DateTime> _clock;

    /// <param name="batchSize">Максимальный размер батча.</param>
    /// <param name="flushInterval">Максимальное время ожидания до принудительного сброса.</param>
    /// <param name="clock">Источник текущего времени (для тестируемости).</param>
    public WriteBatcher(int batchSize, TimeSpan flushInterval, Func<DateTime>? clock = null)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Размер батча должен быть положительным.");
        }

        _batchSize = batchSize;
        _flushInterval = flushInterval;
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// Читать канал до его завершения, формируя батчи и вызывая
    /// <paramref name="flushAsync"/> для каждого набора.
    /// </summary>
    /// <param name="reader">Читатель общего канала фракталов.</param>
    /// <param name="flushAsync">Обработчик накопленного батча.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task RunAsync(
        ChannelReader<FractalRecord> reader,
        Func<List<FractalRecord>, CancellationToken, Task> flushAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(flushAsync);

        var batch = new List<FractalRecord>(_batchSize);

        while (await reader.WaitToReadAsync(cancellationToken))
        {
            var deadline = _clock() + _flushInterval;

            while (batch.Count < _batchSize && reader.TryRead(out var record))
            {
                batch.Add(record);
            }

            // Если батч не набран — ждём до дедлайна, добирая элементы.
            while (batch.Count < _batchSize && _clock() < deadline)
            {
                var remaining = deadline - _clock();
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(remaining);

                try
                {
                    if (!await reader.WaitToReadAsync(timeoutCts.Token))
                    {
                        break;
                    }
                    while (batch.Count < _batchSize && reader.TryRead(out var record))
                    {
                        batch.Add(record);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    break; // дедлайн
                }
            }

            await FlushAsync(batch, flushAsync, cancellationToken);
        }

        // Досбрасываем остаток при завершении канала.
        while (reader.TryRead(out var tail))
        {
            batch.Add(tail);
        }
        await FlushAsync(batch, flushAsync, cancellationToken);
    }

    /// <summary>
    /// Передать текущий батч обработчику и освободить накопитель.
    /// Батчер сам управляет жизненным циклом списка — обработчик не обязан его очищать.
    /// </summary>
    private static async Task FlushAsync(
        List<FractalRecord> batch,
        Func<List<FractalRecord>, CancellationToken, Task> flushAsync,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return;
        }

        await flushAsync(batch, cancellationToken);
        batch.Clear();
    }
}
