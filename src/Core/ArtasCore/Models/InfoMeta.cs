namespace ArtasCore.Models;

/// <summary>
/// Мета-информация о фрактале (накопитель). Прямой перенос Python <c>InfoMeta</c>.
/// Изменяемый класс (в Python — dataclass без frozen).
/// </summary>
public sealed class InfoMeta
{
    public int PickVolume { get; set; }
    public int PickCount { get; set; }
    public int GlassPickCount { get; set; }
    public int GlassPickTotal { get; set; }

    public double Ask { get; set; }
    public double Bid { get; set; }

    public int RACount { get; set; }
    public int RAVolume { get; set; }
    public int RAVolumeBig { get; set; }

    public int SpreadTotal { get; set; }
    public int SpreadPeakCount { get; set; }

    public int AboveCount { get; set; }
    public int BetweenCount { get; set; }
    public int BelowCount { get; set; }

    public int TotalTicks { get; set; }
    public int Volume { get; set; }
    public int VolumeUp { get; set; }
    public int VolumeDown { get; set; }

    /// <summary>Стакан (6 элементов).
    /// 0 — максимальный стакан на пике (Ask), 1 — максимальный стакан на пике (Bid),
    /// 2 — максимальный стакан за фрактал (Ask), 3 — максимальный стакан за фрактал (Bid),
    /// 4 — стакан на последнем тике (Ask), 5 — стакан на последнем тике (Bid).
    /// </summary>
    public List<TempPair> Markets { get; } = CreateDefaultMarkets();

    /// <summary>Предыдущий тик.</summary>
    public MyTick? PrevTick { get; set; }

    /// <summary>
    /// Необязательный делегат логирования (ядро не зависит от конкретного логгера).
    /// </summary>
    public Action<string>? Log { get; set; }

    public InfoMeta()
    {
    }

    private static List<TempPair> CreateDefaultMarkets() =>
        Enumerable.Range(0, 6).Select(_ => new TempPair(0.0, 0)).ToList();

    /// <summary>Обновление состояния (пик).</summary>
    public void Update(MyTick tick)
    {
        if (tick.Volume > 0)
        {
            TotalTicks += 1;
            Volume += (int)tick.Volume;
            if (tick.IsBuy == true)
            {
                VolumeUp += (int)tick.Volume;
            }
            else
            {
                VolumeDown += (int)tick.Volume;
            }
        }

        if (tick.Last > tick.MAsk.Price)
        {
            AboveCount += 1;
        }
        else if (tick.Last < tick.MBid.Price)
        {
            BelowCount += 1;
        }
        else
        {
            BetweenCount += 1;
        }
    }

    /// <summary>Обновление стекла.</summary>
    public void UpdateGlass(MyTick tick, bool isUp, double nextPick)
    {
        SetAb(tick, isUp, nextPick);
        SetMarket(tick, isUp, nextPick);
        UpdateGlassCount(tick, isUp, nextPick);
    }

    /// <summary>Обновление активного пика.</summary>
    public void UpdateActive(MyTick tick, bool isUp, double nextPick)
    {
        SetAb(tick, isUp, nextPick);
        SetMarket(tick, isUp, nextPick);
        UpdateGlassCount(tick, isUp, nextPick);
        Update(tick);

        if (isUp)
        {
            if (tick.IsBuy == false && tick.Sell == nextPick)
            {
                PickVolume += (int)tick.Volume;
                PickCount += 1;
            }
            if (tick.Buy == nextPick)
            {
                RAVolume += (int)tick.Volume;
                RACount += 1;
            }
            if (tick.Buy > nextPick)
            {
                RAVolumeBig += (int)tick.Volume;
            }
        }
        else
        {
            if (tick.IsBuy == true && tick.Buy == nextPick)
            {
                PickVolume += (int)tick.Volume;
                PickCount += 1;
            }
            if (tick.Sell == nextPick)
            {
                RAVolume += (int)tick.Volume;
                RACount += 1;
            }
            if (tick.Sell < nextPick)
            {
                RAVolumeBig += (int)tick.Volume;
            }
        }
    }

    private void UpdateGlassCount(MyTick tick, bool isUp, double nextPick)
    {
        if (isUp)
        {
            if (tick.MBid.Price < nextPick)
            {
                GlassPickCount += 1;
            }
        }
        else
        {
            if (tick.MAsk.Price > nextPick)
            {
                GlassPickCount += 1;
            }
        }
    }

    public void LogF(MyTick tick, bool isUp, double pick, string text)
    {
        Log?.Invoke($"Error {text}: {isUp} {pick} {tick} {PrevTick}");
    }

    private void SetMarket(MyTick tick, bool isUp, double nextPick)
    {
        if (tick.MAsk.Price == 0)
        {
            return;
        }

        Markets[4].UpdateMp(tick.MAsk);
        Markets[5].UpdateMp(tick.MBid);

        if (Markets[0].Price == 0)
        {
            Markets[0].UpdateMp(tick.MAsk);
            Markets[1].UpdateMp(tick.MBid);
            Markets[2].UpdateMp(tick.MAsk);
            Markets[3].UpdateMp(tick.MBid);
            Markets[4].UpdateMp(tick.MAsk);
            Markets[5].UpdateMp(tick.MBid);
        }

        if (isUp)
        {
            if (Markets[1].Price > tick.MBid.Price)
            {
                Markets[0].UpdateMp(tick.MAsk);
                Markets[1].UpdateMp(tick.MBid);
            }
            else if (Markets[1].Price == tick.MBid.Price)
            {
                var sumVolume = Markets[0].Volume + Markets[1].Volume;
                if (tick.Sum > sumVolume)
                {
                    Markets[0].UpdateMp(tick.MAsk);
                    Markets[1].UpdateMp(tick.MBid);
                }
            }
        }
        else
        {
            if (Markets[0].Price < tick.MAsk.Price)
            {
                Markets[0].UpdateMp(tick.MAsk);
                Markets[1].UpdateMp(tick.MBid);
            }
            else if (Markets[0].Price == tick.MAsk.Price)
            {
                var sumVolume = Markets[0].Volume + Markets[1].Volume;
                if (tick.Sum > sumVolume)
                {
                    Markets[0].UpdateMp(tick.MAsk);
                    Markets[1].UpdateMp(tick.MBid);
                }
            }
        }

        var sumVolume2 = Markets[2].Volume + Markets[3].Volume;
        if (tick.Sum > sumVolume2)
        {
            Markets[2].UpdateMp(tick.MAsk);
            Markets[3].UpdateMp(tick.MBid);
        }
        PrevTick = tick;
    }

    private void SetAb(MyTick tick, bool isUp, double nextPick)
    {
        if (isUp)
        {
            if (Ask == 0.0 || Ask > tick.Ask)
            {
                Ask = tick.Ask;
            }
            if (Bid == 0.0 || Bid > tick.Bid)
            {
                Bid = tick.Bid;
            }
        }
        else
        {
            if (Ask == 0.0 || Ask < tick.Ask)
            {
                Ask = tick.Ask;
            }
            if (Bid == 0.0 || Bid < tick.Bid)
            {
                Bid = tick.Bid;
            }
        }

        var spread = tick.Ask - tick.Bid;
        if (spread > 0.00001)
        {
            SpreadPeakCount += 1;
        }
    }

    /// <summary>Завершение пика.</summary>
    public void OnPickCompleted()
    {
        GlassPickTotal += GlassPickCount;
        SpreadTotal += SpreadPeakCount;
        ResetTransientCounters();
    }

    /// <summary>Добавить информацию из другого <see cref="InfoMeta"/>.</summary>
    public void Add(InfoMeta other)
    {
        TotalTicks += other.TotalTicks;
        Volume += other.Volume;
        VolumeUp += other.VolumeUp;
        VolumeDown += other.VolumeDown;
        AboveCount += other.AboveCount;
        BetweenCount += other.BetweenCount;
        BelowCount += other.BelowCount;
    }

    /// <summary>Сброс всех счётчиков.</summary>
    public void ResetAllState()
    {
        ResetTransientCounters();
        foreach (var m in Markets)
        {
            m.Clear();
        }

        GlassPickTotal = 0;
        SpreadTotal = 0;
        TotalTicks = 0;
        Volume = 0;
        VolumeUp = 0;
        VolumeDown = 0;
        AboveCount = 0;
        BetweenCount = 0;
        BelowCount = 0;
    }

    /// <summary>Сброс временных счётчиков.</summary>
    public void ResetTransientCounters()
    {
        SpreadPeakCount = 0;
        RAVolumeBig = 0;
        RACount = 0;
        RAVolume = 0;
        PickVolume = 0;
        PickCount = 0;
        GlassPickCount = 0;
        Ask = 0.0;
        Bid = 0.0;
    }
}
