namespace ArtasWriter.Application.Buffers;

/// <summary>
/// Циклический буфер фиксированного размера. Аналог Python <c>ListClass</c>.
/// При переполнении вытесняет самые старые элементы.
/// </summary>
/// <typeparam name="T">Тип элемента.</typeparam>
public sealed class CircularBuffer<T>
{
    private readonly LinkedList<T> _items = new();
    private readonly int _capacity;

    public CircularBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Емкость должна быть положительной.");
        }
        _capacity = capacity;
    }

    /// <summary>Емкость буфера.</summary>
    public int Capacity => _capacity;

    /// <summary>Текущее количество элементов.</summary>
    public int Count => _items.Count;

    /// <summary>Добавить элемент (при переполнении вытесняется самый старый).</summary>
    public void Add(T item)
    {
        _items.AddLast(item);
        while (_items.Count > _capacity)
        {
            _items.RemoveFirst();
        }
    }

    /// <summary>Очистить буфер.</summary>
    public void Clear() => _items.Clear();

    /// <summary>Снимок содержимого (от старых к новым).</summary>
    public IReadOnlyList<T> ToList() => _items.ToList();

    /// <summary>Последний элемент или default, если буфер пуст.</summary>
    public T? LastOrDefault() => _items.Count > 0 ? _items.Last!.Value : default;
}
