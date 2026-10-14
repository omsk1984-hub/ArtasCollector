using ArtasCore.Models;

namespace ArtasChart.Services;

/// <summary>
/// Реализация фильтрации фракталов по объёму и стаканам (Ask/Bid).
/// Использует именованные позиции стакана из <see cref="InfoF.GetMarket"/>.
/// Применяется к «красным» точкам (невзятиям): скрывает те, что не проходят
/// порог по объёму стакана.
/// </summary>
public sealed class FractalFilter : IFractalFilter
{
    public IReadOnlyList<Fractal> Filter(IReadOnlyList<Fractal> source, FractalFilterOptions options)
    {
        if (options is null || !options.HasActiveThresholds)
        {
            return source;
        }

        return source
            .Where(f => PassesVolume(f, options))
            .ToList();
    }

    /// <inheritdoc />
    public bool PassesVolume(Fractal fractal, FractalFilterOptions options)
    {
        if (options is null || !options.HasActiveThresholds)
        {
            return true;
        }

        return PassesMinVolume(fractal, options) ||
               PassesMinPickVolume(fractal, options) ||
               PassesMinMarkets(fractal, options);
    }

    /// <inheritdoc />
    public bool PassesMinVolume(Fractal fractal, FractalFilterOptions options)
    {
        // Порог по Info.Volume не задан — красные точки не отображаются.
        if (options is null || options.MinVolume <= 0)
        {
            return false;
        }

        return fractal.Info.Volume > options.MinVolume;
    }

    /// <inheritdoc />
    public bool PassesMinPickVolume(Fractal fractal, FractalFilterOptions options)
    {
        // Порог по Info.PickVolume не задан — по этому критерию точка не проходит.
        if (options is null || options.MinPickVolume <= 0)
        {
            return false;
        }

        return fractal.Info.PickVolume > options.MinPickVolume;
    }

    /// <inheritdoc />
    public bool PassesMinMarkets(Fractal fractal, FractalFilterOptions options)
    {
        // Оба порога по стакану выключены — зелёные точки не отображаются.
        if (options is null ||
            (options.MinMarket0Volume <= 0 && options.MinMarket1Volume <= 0))
        {
            return false;
        }

        // Верхний фрактал — фильтр по стакану Ask (Markets[0]).
        if (options.MinMarket0Volume > 0 &&
            fractal.Info.GetMarket(0).Volume > options.MinMarket0Volume)
        {
            return true;
        }

        // Нижний фрактал — фильтр по стакану Bid (Markets[1]).
        if (options.MinMarket1Volume > 0 &&
            fractal.Info.GetMarket(1).Volume > options.MinMarket1Volume)
        {
            return true;
        }

        return false;
    }
}
