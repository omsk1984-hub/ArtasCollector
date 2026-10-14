namespace ArtasCore.Abstractions;

/// <summary>
/// Защита от одновременной работы нескольких экземпляров сервиса.
///
/// Назначение: при параллельной работе двух Writer каждый процесс держит
/// собственный <c>TickService</c>, и первый фрактал после старта задаёт базу
/// интереса дня равной своему интересу — это даёт некорректный
/// <c>from_start_interest = 0</c> в середине дня (см.
/// <c>db/recalculate-from-start-interest.sql</c>). Guard не даёт второму
/// экземпляру запустить конвейеры записи.
///
/// Реализация по умолчанию — session-level advisory-lock PostgreSQL
/// (<c>pg_try_advisory_lock</c>): блокировка автоматически снимается при
/// обрыве/завершении соединения, в том числе при аварийном падении процесса.
/// </summary>
public interface ISingleInstanceGuard : IAsyncDisposable
{
    /// <summary>
    /// Попытаться получить эксклюзивную блокировку экземпляра.
    /// </summary>
    /// <returns>
    /// true — блокировка получена (можно запускать конвейеры); false — уже
    /// работает другой экземпляр (запуск нужно прервать).
    /// </returns>
    Task<bool> TryAcquireAsync(CancellationToken cancellationToken = default);
}
