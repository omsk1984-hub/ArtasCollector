using ArtasWriter.Application.Buffers;
using ArtasCore.Models;

namespace ArtasWriter.Application.Services;

/// <summary>
/// Базовый сервис управления циклическим буфером фракталов.
/// Прямой перенос Python <c>FractalBufferService</c>: индексация, DefInterest,
/// FromStartInterest и уведомление подписчиков.
///
/// ВАЖНО: вся логика (границы дня, база интереса дня, расчёт FromStartInterest)
/// ведётся по UTC. МСК (UTC+3, <see cref="MyTick.TimeMsk"/>) используется только
/// для отображения/визуализации. Определение календарного дня дня делается по
/// <see cref="MyTick.TimeUtc"/>, чтобы граница суток совпадала с UTC-датой
/// хранения (<c>public.fractals.time</c>).
/// </summary>
public class FractalBufferService
{
    private readonly CircularBuffer<Fractal> _fractals;
    private readonly List<Action<Fractal>> _callbacks = new();

    private DateOnly? _currentDay;
    private long _dayStartInterest;

    public FractalBufferService(int bufferSize = 500)
    {
        _fractals = new CircularBuffer<Fractal>(bufferSize);
    }

    /// <summary>Текущий (последний добавленный) фрактал.</summary>
    public Fractal? FractalCurrent { get; private set; }

    /// <summary>
    /// Установить базу открытого интереса на начало дня (значение самого раннего
    /// боевого фрактала дня, полученное из БД при старте/перезапуске Writer).
    /// Пока база не установлена (или установлена для другого дня), <see cref="AddFractal"/>
    /// использует интерес первого полученного фрактала дня (текущее поведение).
    /// </summary>
    /// <param name="day">Календарный день (МСК), к которому относится база.</param>
    /// <param name="dayStartInterest">Открытый интерес на начало дня.</param>
    public void SetDayStartInterest(DateOnly day, long dayStartInterest)
    {
        _currentDay = day;
        _dayStartInterest = dayStartInterest;
    }

    /// <summary>ID последнего добавленного фрактала.</summary>
    public long LastId { get; private set; }

    /// <summary>Снимок буфера фракталов.</summary>
    public IReadOnlyList<Fractal> Fractals => _fractals.ToList();

    /// <summary>Количество фракталов в буфере.</summary>
    public int Count => _fractals.Count;

    /// <summary>
    /// Добавить фрактал в буфер с валидацией, индексацией и расчётом интересов.
    /// Аналог Python <c>add_fractal()</c>.
    /// </summary>
    /// <returns>true, если фрактал добавлен; false — если пропущен.</returns>
    public bool AddFractal(Fractal fractal)
    {
        ArgumentNullException.ThrowIfNull(fractal);

        if (FractalCurrent is not null)
        {
            if (FractalCurrent.IsUp == fractal.IsUp)
            {
                return false;
            }
            if (FractalCurrent.Id >= fractal.Id)
            {
                return false;
            }
        }

        _fractals.Add(fractal);
        fractal.Index = _fractals.Count;

        // Прирост интереса от начала дня. День считаем по UTC (TimeUtc),
        // а не по МСК: границы дня должны совпадать с UTC-датой хранения
        // (public.fractals.time). МСК используется только для визуализации.
        var day = DateOnly.FromDateTime(fractal.TickPeak.TimeUtc);
        if (_currentDay != day)
        {
            _currentDay = day;
            _dayStartInterest = fractal.Interest;
        }
        fractal.FromStartInterest = fractal.Interest - _dayStartInterest;

        if (FractalCurrent is not null)
        {
            fractal.DefInterest = fractal.Interest - FractalCurrent.Interest;
        }

        FractalCurrent = fractal;
        LastId = fractal.Id;

        Emit(fractal);
        return true;
    }

    /// <summary>Подписаться на событие нового фрактала.</summary>
    public void AddFractalCallback(Action<Fractal> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (!_callbacks.Contains(callback))
        {
            _callbacks.Add(callback);
        }
    }

    /// <summary>Отписаться от события.</summary>
    public void RemoveFractalCallback(Action<Fractal> callback)
    {
        _callbacks.Remove(callback);
    }

    /// <summary>Очистить буфер и состояние.</summary>
    public void Reset()
    {
        _fractals.Clear();
        FractalCurrent = null;
        LastId = 0;
        _currentDay = null;
        _dayStartInterest = 0;
    }

    private void Emit(Fractal fractal)
    {
        foreach (var cb in _callbacks.ToArray())
        {
            cb(fractal);
        }
    }
}
