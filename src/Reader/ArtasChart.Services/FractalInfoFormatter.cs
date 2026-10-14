using System.Text;
using ArtasCore.Models;

namespace ArtasChart.Services;

/// <summary>
/// Реализация форматирования info-панели по выбранному фракталу.
/// Использует именованные позиции стакана вместо «магических» индексов.
/// </summary>
public sealed class FractalInfoFormatter : IFractalInfoFormatter
{
    public string Format(Fractal? fractal)
    {
        if (fractal is null)
        {
            return "Наведите курсор на точку...";
        }

        var sb = new StringBuilder();

        // Дата/время фрактала (МСК) сразу после номера и направления.
        var peakTime = fractal.TickPeak.TimeMsk;
        sb.AppendLine($"#{fractal.Index} {(fractal.IsUp ? "UP" : "DOWN")}");
        sb.AppendLine($"Date: {peakTime:yyyy-MM-dd HH:mm:ss.fff} MSK");
        sb.AppendLine($"Id: {fractal.Id}");
        sb.AppendLine($"Distance: {fractal.Distance}");
        sb.AppendLine($"Interest: {fractal.Interest}");
        sb.AppendLine($"Def Interest: {fractal.DefInterest}");
        sb.AppendLine($"From Start Interest: {fractal.FromStartInterest}");
        sb.AppendLine();

        sb.AppendLine($"Volume: {fractal.Info.Volume}");
        sb.AppendLine($"Volume UP: {fractal.Info.VolumeUp}");
        sb.AppendLine($"Glass Pick Count: {fractal.Info.GlassPickCount}");
        sb.AppendLine();
        sb.AppendLine($"   Price: {fractal.Price}-{fractal.Info.PickVolume} count-{fractal.Info.PickCount}");
        sb.AppendLine($"LM_v_Ask: {Market(fractal, 0)}");
        sb.AppendLine($"LM_v_Bid: {Market(fractal, 1)}");
        sb.AppendLine($" M_v_Ask: {Market(fractal, 2)}");
        sb.AppendLine($" M_v_Bid: {Market(fractal, 3)}");
        sb.AppendLine($" L_v_Ask: {Market(fractal, 4)}");
        sb.AppendLine($" L_v_Bid: {Market(fractal, 5)}");
        sb.AppendLine();
        //sb.AppendLine($"Pick Volume: {fractal.Info.PickVolume}");
        //sb.AppendLine($"Pick Count: {fractal.Info.PickCount}");
        
        sb.AppendLine($"RA Count: {fractal.Info.RACount}");
        sb.AppendLine($"RA Volume: {fractal.Info.RAVolume}");
        sb.AppendLine($"Spread Total: {fractal.Info.SpreadTotal}");
        
        sb.AppendLine();
        sb.AppendLine($"price open: {fractal.Stat.PriceOpen}");
        sb.AppendLine($"price stop: {fractal.Stat.PriceStop}");
        sb.AppendLine($"price take: {fractal.Stat.PriceTake}");
        sb.AppendLine($"result stop: {fractal.Stat.ResultStop}");
        sb.AppendLine($"result take: {fractal.Stat.ResultTake}");
        sb.AppendLine($"index open: {fractal.Stat.IndexOpen}");
        sb.AppendLine($"index stop: {fractal.Stat.IndexStop}");
        sb.AppendLine($"index take: {fractal.Stat.IndexTake}");
        sb.AppendLine($"index total: {fractal.Stat.IndexTotal}");

        return sb.ToString();
    }

    /// <summary>
    /// Одна компактная строка-сводка для копирования: номер, направление, время,
    /// цена, стакан последнего тика пика (Ask/Bid), Volume и результат сделки.
    /// </summary>
    public string FormatClipboard(Fractal? fractal)
    {
        if (fractal is null)
        {
            return string.Empty;
        }

        var peakTime = fractal.TickPeak.TimeMsk;
        var ask = fractal.Info.MarketAsk;
        var bid = fractal.Info.MarketBid;

        return $"#{fractal.Index} {(fractal.IsUp ? "UP" : "DOWN")} " +
               $"{peakTime:yyyy-MM-dd HH:mm:ss.fff} MSK | " +
               $"Id={fractal.Id} Price={fractal.Price} " +
               $"Distance={fractal.Distance} Interest={fractal.Interest} " +
               $"FSI={fractal.FromStartInterest} | " +
               $"MAsk={ask.Price}-{ask.Volume} MBid={bid.Price}-{bid.Volume} " +
               $"Volume={fractal.Info.Volume} | " +
               $"Take={fractal.Stat.IsTake} ResultTake={fractal.Stat.ResultTake} " +
               $"ResultStop={fractal.Stat.ResultStop}";
    }

    private static string Market(Fractal fractal, int index)
    {
        var m = fractal.Info.GetMarket(index);
        return $"{m.Price}-{m.Volume}";
    }
}