namespace Stat.Application.Buffers;

/// <summary>
/// Циклический буфер фиксированного размера с доступом по абсолютному индексу
/// и счётчиком общего числа добавленных элементов. Прямой перенос Python
/// <c>artas2026.utils.list_class.ListClass</c> в объёме, необходимом Stat-сервису.
/// </summary>
/// <typeparam name="T">Тип элемента.</typeparam>
/// <remarks>
/// Отличия от Python-эталона, важные для совместимости:
/// <list type="bullet">
/// <item><description><see cref="Count"/> — общее число добавленных элементов (растёт бесконечно),
/// а не размер живого окна; используется в <c>_on_fractal</c> как <c>fractals.Count</c>.</description></item>
/// <item><description><see cref="GetByIndex"/> воспроизводит Python <c>get_by_index(index)</c>:
/// <c>self[Count - index - 1]</c>, то есть индекс 0 — самый старый из живых элементов.</description></item>
/// <item><description><see cref="GetByOffset"/> воспроизводит Python <c>self[i]</c>:
/// 0 — последний добавленный, 1 — предпоследний и т.д.</description></item>
/// </list>
/// </remarks>
public sealed class CircularBuffer<T>
{
    private readonly T?[] _items;
    private readonly int _capacity;

    /// <summary>Индекс последнего добавленного элемента (позиция записи), -1 пока пусто.</summary>
    private int _index = -1;

    /// <summary>Индекс первого (самого старого) элемента в живом окне.</summary>
    private int _startIndex;

    /// <summary>Число фактически заполненных ячеек.</summary>
    private int _filled;

    public CircularBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Емкость должна быть положительной.");
        }

        _capacity = capacity;
        _items = new T?[capacity];
    }

    /// <summary>Емкость буфера.</summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Общее количество добавленных элементов (в Python — <c>ListClass.Count</c>).
    /// Растёт независимо от вытеснения старых элементов.
    /// </summary>
    public long Count { get; private set; }

    /// <summary>Число элементов в живом окне (<= <see cref="Capacity"/>).</summary>
    public int LiveCount => _filled;

    /// <summary>Индекс последнего добавленного элемента (в Python — <c>ListClass.Index</c>).</summary>
    public int Index => _index;

    /// <summary>Добавить элемент (при переполнении вытесняется самый старый).</summary>
    public void Add(T item)
    {
        var wasFull = _filled >= _capacity;
        if (wasFull)
        {
            _startIndex = (_startIndex + 1) % _capacity;
        }
        else
        {
            _filled++;
        }

        Count++;
        _index = _index == _capacity - 1 ? 0 : _index + 1;
        _items[_index] = item;

        if (Count == 1)
        {
            _startIndex = _index;
        }
    }

    /// <summary>
    /// Получить элемент по абсолютному индексу (0 — самый старый из живых).
    /// Прямой аналог Python <c>ListClass.get_by_index(index)</c>.
    /// </summary>
    /// <returns>Элемент или default, если индекс вне живого окна.</returns>
    public T? GetByIndex(long index)
    {
        var newIndex = Count - index - 1;
        if (newIndex >= 0 && newIndex < _capacity)
        {
            return GetByOffset((int)newIndex);
        }

        return default;
    }

    /// <summary>
    /// Получить элемент со смещением от последнего добавленного (0 — последний).
    /// Аналог Python <c>ListClass.__getitem__(i)</c>.
    /// </summary>
    public T? GetByOffset(int offset)
    {
        if (_capacity == 0)
        {
            return default;
        }

        var index = _index - offset;
        if (index < 0)
        {
            index += _capacity;
        }

        return _items[index];
    }

    /// <summary>Снимок живого окна в порядке от старого к новому.</summary>
    public IReadOnlyList<T> ToList()
    {
        var result = new List<T>(_filled);
        for (var i = 0; i < _filled; i++)
        {
            var abs = (_startIndex + i) % _capacity;
            if (_items[abs] is { } item)
            {
                result.Add(item);
            }
        }

        return result;
    }

    /// <summary>Очистить буфер и счётчики.</summary>
    public void Clear()
    {
        Array.Clear(_items, 0, _capacity);
        _index = -1;
        _startIndex = 0;
        _filled = 0;
        Count = 0;
    }
}
