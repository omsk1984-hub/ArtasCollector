using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArtasChart.Services;

/// <summary>
/// Периодический опрос источника данных (real-time обновление).
/// Абстрагирует таймер, чтобы ViewModel не зависел от UI-фреймворка
/// (Avalonia DispatcherTimer) и легко тестировался.
/// </summary>
public interface IPollingService : IAsyncDisposable
{
    /// <summary>
    /// Запустить периодический вызов <paramref name="callback"/> с заданным интервалом.
    /// Вызывается не чаще одного раза за интервал; предыдущий вызов должен завершиться.
    /// </summary>
    void Start(TimeSpan interval, Func<CancellationToken, Task> callback);

    /// <summary>Остановить периодический опрос.</summary>
    void Stop();

    /// <summary>Признак активности опроса.</summary>
    bool IsRunning { get; }
}
