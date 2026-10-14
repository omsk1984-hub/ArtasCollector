namespace ArtasWriter.Infrastructure.Services;

/// <summary>
/// Дневной счётчик записанных фракталов. Инкапсулирует политику смены дня:
/// при переходе на следующую календарную дату счётчик обнуляется.
/// </summary>
public interface IDailyCounter
{
    /// <summary>Количество записанных фракталов за текущий день.</summary>
    int WrittenToday { get; }

    /// <summary>Учесть <paramref name="count"/> записанных фракталов (со сбросом при смене дня).</summary>
    void Add(int count);
}
