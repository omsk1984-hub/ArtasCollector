namespace ArtasChart.Data;

/// <summary>
/// Пользовательские настройки фильтра фракталов для конкретного символа.
/// Сериализуется целиком в одно JSONB-поле <c>symbol_filter_settings.settings</c>.
/// </summary>
/// <param name="MinMarket0Volume">Порог по Markets[0].Volume (Ask). 0 — выключен.</param>
/// <param name="MinMarket1Volume">Порог по Markets[1].Volume (Bid). 0 — выключен.</param>
/// <param name="MinVolume">Порог по Info.Volume. 0 — выключен.</param>
/// <param name="MinPickVolume">Порог по Info.PickVolume. 0 — выключен.</param>
/// <param name="ExtremumNeighbors">Число соседей N фильтра локального экстремума (0 — выключен).</param>
/// <param name="ShowRedPoints">Показывать ли точки-невзятия (красные/зелёные).</param>
/// <param name="LoadLimit">Лимит загружаемых фракталов (последние N записей).</param>
public sealed record FractalFilterSettings(
    double MinMarket0Volume = 0,
    double MinMarket1Volume = 0,
    double MinVolume = 0,
    int ExtremumNeighbors = 2,
    bool ShowRedPoints = false,
    double MinPickVolume = 0,
    int LoadLimit = 5000);
