namespace ArtasCore.Models;

/// <summary>
/// Временная пара (цена/объем) для накопления в <see cref="InfoMeta"/>.
/// Аналог Python <c>TempPair</c> (dataclass slots).
/// </summary>
public sealed class TempPair
{
    public double Price { get; set; }
    public int Volume { get; set; }

    public TempPair() { }

    public TempPair(double price, int volume)
    {
        Price = price;
        Volume = volume;
    }

    /// <summary>Обновить значения из <see cref="MarketPair"/>.</summary>
    public void UpdateMp(MarketPair pair)
    {
        Price = pair.Price;
        Volume = pair.Volume;
    }

    /// <summary>Сбросить значения.</summary>
    public void Clear()
    {
        Price = 0.0;
        Volume = 0;
    }
}
