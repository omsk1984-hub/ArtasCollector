using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArtasChart.Services;

/// <summary>
/// Реализация периодического опроса на основе <see cref="PeriodicTimer"/>.
/// Не зависит от UI-фреймворка: callback выполняется в фоне, а маршалинг
/// в UI-поток (если нужен) выполняет вызывающая сторона (ViewModel/View).
/// </summary>
public sealed class PollingService : IPollingService
{
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <inheritdoc />
    public bool IsRunning { get; private set; }

    /// <inheritdoc />
    public void Start(TimeSpan interval, Func<CancellationToken, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        Stop();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        IsRunning = true;

        _loop = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(interval);
            try
            {
                // Первый вызов — сразу, затем по интервалу.
                await callback(token);
                while (await timer.WaitForNextTickAsync(token))
                {
                    await callback(token);
                }
            }
            catch (OperationCanceledException)
            {
                // Остановка — ожидаемое поведение.
            }
            finally
            {
                IsRunning = false;
            }
        }, token);
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (_cts is null)
        {
            return;
        }

        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Уже освобождён — игнорируем.
        }

        _cts.Dispose();
        _cts = null;
        _loop = null;
        IsRunning = false;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Stop();
        if (_loop is not null)
        {
            try
            {
                await _loop;
            }
            catch (OperationCanceledException)
            {
                // Ожидаемо при отмене.
            }
        }
    }
}
