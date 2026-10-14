namespace ArtasWriter.Infrastructure.Services;

/// <summary>
/// Реализация <see cref="IDailyCounter"/>. Сбрасывает счётчик при смене
/// календарной даты (UTC). Не потокобезопасен по дизайну: используется
/// единственным writer-консьюмером.
/// </summary>
public sealed class DailyCounter : IDailyCounter
{
    private readonly Func<DateTime> _clock;
    private DateOnly _counterDay = DateOnly.FromDateTime(DateTime.UtcNow);
    private int _writtenToday;

    /// <param name="clock">Источник текущего времени (для тестируемости).</param>
    public DailyCounter(Func<DateTime>? clock = null)
    {
        _clock = clock ?? (() => DateTime.UtcNow);
        _counterDay = DateOnly.FromDateTime(_clock());
    }

    /// <inheritdoc />
    public int WrittenToday => _writtenToday;

    /// <inheritdoc />
    public void Add(int count)
    {
        var today = DateOnly.FromDateTime(_clock());
        if (today != _counterDay)
        {
            _counterDay = today;
            _writtenToday = 0;
        }

        _writtenToday += count;
    }
}
