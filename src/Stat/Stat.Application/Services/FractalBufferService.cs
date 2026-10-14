using ArtasCore.Models;
using Stat.Application.Buffers;

namespace Stat.Application.Services;

/// <summary>
/// Базовый сервис управления циклическим буфером фракталов.
/// Прямой перенос Python <c>artas2026.services.fractal_buffer.FractalBufferService</c>:
/// валидация, индексация, DefInterest, уведомление подписчиков.
/// </summary>
/// <remarks>
/// В отличие от Writer-версии, callback получает сам буфер
/// (<see cref="CircularBuffer{T}"/>), что необходимо stat-сервису для доступа
/// по абсолютному индексу (<c>get_by_index</c>) и общего числа (<c>Count</c>).
/// </remarks>
public class FractalBufferService
{
    private readonly CircularBuffer<Fractal> _fractals;
    private readonly List<Action<Fractal, CircularBuffer<Fractal>>> _callbacks = new();

    public FractalBufferService(int bufferSize = 500)
    {
        _fractals = new CircularBuffer<Fractal>(bufferSize);
    }

    /// <summary>Текущий (последний добавленный) фрактал.</summary>
    public Fractal? FractalCurrent { get; private set; }

    /// <summary>ID последнего добавленного фрактала.</summary>
    public long LastId { get; private set; }

    /// <summary>Снимок живого окна буфера (от старого к новому).</summary>
    public IReadOnlyList<Fractal> Fractals => _fractals.ToList();

    /// <summary>Общее количество добавленных фракталов (в Python — <c>Fractals.Count</c>).</summary>
    public long FractalsCount => _fractals.Count;

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
        fractal.Index = (int)_fractals.Count;

        if (FractalCurrent is not null)
        {
            fractal.DefInterest = fractal.Interest - FractalCurrent.Interest;
        }

        FractalCurrent = fractal;
        LastId = fractal.Id;

        Emit(fractal);
        return true;
    }

    /// <summary>
    /// Подписаться на событие нового фрактала. Callback получает добавленный
    /// фрактал и текущий буфер.
    /// </summary>
    public void AddFractalCallback(Action<Fractal, CircularBuffer<Fractal>> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (!_callbacks.Contains(callback))
        {
            _callbacks.Add(callback);
        }
    }

    /// <summary>Отписаться от события.</summary>
    public void RemoveFractalCallback(Action<Fractal, CircularBuffer<Fractal>> callback)
    {
        _callbacks.Remove(callback);
    }

    /// <summary>Очистить буфер и состояние.</summary>
    public void Reset()
    {
        _fractals.Clear();
        FractalCurrent = null;
        LastId = 0;
    }

    private void Emit(Fractal fractal)
    {
        foreach (var cb in _callbacks.ToArray())
        {
            cb(fractal, _fractals);
        }
    }
}
