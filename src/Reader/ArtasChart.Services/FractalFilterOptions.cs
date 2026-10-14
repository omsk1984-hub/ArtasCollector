namespace ArtasChart.Services;

/// <summary>
/// Параметры фильтрации фракталов. Все пороги «выключены», когда равны нулю.
/// </summary>
/// <param name="MinMarket0Volume">Порог по стакану последнего тика пика (Ask). 0 — выключен.</param>
/// <param name="MinMarket1Volume">Порог по стакану последнего тика пика (Bid). 0 — выключен.</param>
/// <param name="MinVolume">Порог по Info.Volume. 0 — выключен.</param>
/// <param name="MinPickVolume">Порог по Info.PickVolume. 0 — выключен.</param>
public sealed record FractalFilterOptions(
    double MinMarket0Volume = 0,
    double MinMarket1Volume = 0,
    double MinVolume = 0,
    double MinPickVolume = 0)
{
    /// <summary>
    /// Хотя бы один порог активен (> 0). Если ни один порог не задан,
    /// объёмная фильтрация не применяется.
    /// </summary>
    public bool HasActiveThresholds =>
        MinMarket0Volume > 0 || MinMarket1Volume > 0 ||
        MinVolume > 0 || MinPickVolume > 0;
}
