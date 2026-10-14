using System.Text.Json;
using ArtasCore.Models;
using Xunit;

namespace ArtasWriter.Tests.Core;

/// <summary>
/// Тесты моделей Domain: свойства, флаги, создание InfoF.
/// </summary>
public class ModelTests
{
    [Theory]
    [InlineData(32, true)]   // TICK_FLAG_BUY
    [InlineData(64, false)]  // TICK_FLAG_SELL
    [InlineData(0, null)]
    [InlineData(2, null)]    // только BID
    [InlineData(96, true)]   // и BUY, и SELL — побеждает BUY (проверка порядка)
    public void TickFlag_Direction_FromRawInt_ReturnsExpected(int flags, bool? expected)
    {
        Assert.Equal(expected, ((TickFlag)flags).Direction());
    }

    [Fact]
    public void MyTick_BuyAndSell_DependOnDirection()
    {
        var buyTick = new MyTick { Last = 1.234, Flags = (int)TickFlag.Buy };
        var sellTick = new MyTick { Last = 1.234, Flags = (int)TickFlag.Sell };
        var neutralTick = new MyTick { Last = 1.234, Flags = 0 };

        Assert.Equal(1.234, buyTick.Buy);
        Assert.Equal(0.0, buyTick.Sell);

        Assert.Equal(0.0, sellTick.Buy);
        Assert.Equal(1.234, sellTick.Sell);

        Assert.Equal(0.0, neutralTick.Buy);
        Assert.Equal(0.0, neutralTick.Sell);
        Assert.Null(neutralTick.IsBuy);
    }

    [Fact]
    public void MyTick_Time_FromTimeMsc()
    {
        var tick = new MyTick { TimeMsc = 1_700_000_000_000 };

        // TimeUtc — время тика в UTC.
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000).UtcDateTime, tick.TimeUtc);

        // TimeMsk — московское время (UTC+3), используется в UI-представлениях.
        Assert.Equal(tick.TimeUtc.Add(TimeSpan.FromHours(3)), tick.TimeMsk);
    }

    [Fact]
    public void MarketPairBufferItem_Sum_AddsVolumes()
    {
        var item = new MarketPairBufferItem
        {
            MarketPairs = new[]
            {
                new MarketPair(1.0, 10),
                new MarketPair(0.9, 20),
            },
        };

        Assert.Equal(30, item.Sum);
        Assert.Equal(1.0, item.MAsk.Price);
        Assert.Equal(0.9, item.MBid.Price);
    }

    [Fact]
    public void InfoF_Create_MapsAllFields()
    {
        var meta = new InfoMeta
        {
            PickVolume = 5,
            PickCount = 2,
            GlassPickCount = 3,
            GlassPickTotal = 7,
            Ask = 1.1,
            Bid = 1.0,
            RACount = 4,
            RAVolume = 40,
            RAVolumeBig = 400,
            SpreadTotal = 9,
            SpreadPeakCount = 8,
            Volume = 100,
            VolumeUp = 60,
            VolumeDown = 40,
            TotalTicks = 15,
            AboveCount = 1,
            BetweenCount = 2,
            BelowCount = 3,
        };

        var info = InfoF.Create(meta);

        Assert.Equal(5, info.PickVolume);
        Assert.Equal(2, info.PickCount);
        Assert.Equal(7, info.GlassPickTotal);
        Assert.Equal(1.1, info.Ask);
        Assert.Equal(100, info.Volume);
        Assert.Equal(6, info.Markets.Count);
    }

    [Fact]
    public void Fractal_Constructor_SetsIdFromTimeMsc()
    {
        var tick = new MyTick { TimeMsc = 123456789 };

        var fractal = new Fractal(isUp: true, tick);

        Assert.True(fractal.IsUp);
        Assert.Equal(123456789, fractal.Id);
        Assert.Same(tick, fractal.TickPeak);
    }

    [Fact]
    public void Fractal_Interest_FromPeak()
    {
        var tick = new MyTick { Interest = 4242 };
        var fractal = new Fractal(isUp: true, tick);

        Assert.Equal(4242, fractal.Interest);
    }

    // --- TickFlag / TickFlagExtensions ---

    [Theory]
    [InlineData(TickFlag.Buy, true)]
    [InlineData(TickFlag.Sell, false)]
    [InlineData(TickFlag.None, null)]
    [InlineData(TickFlag.Bid, null)]
    [InlineData(TickFlag.Buy | TickFlag.Sell, true)] // BUY имеет приоритет
    public void TickFlag_Direction_ReturnsExpected(TickFlag flags, bool? expected)
    {
        Assert.Equal(expected, flags.Direction());
    }

    [Fact]
    public void TickFlag_HasAllAndHasAny_Work()
    {
        var flags = TickFlag.Last | TickFlag.Volume | TickFlag.Buy;

        Assert.True(flags.HasAll(TickFlag.Last | TickFlag.Buy));
        Assert.False(flags.HasAll(TickFlag.Last | TickFlag.Sell));
        Assert.True(flags.HasAny(TickFlag.Sell | TickFlag.Volume));
        Assert.False(flags.HasAny(TickFlag.Ask));
    }

    [Fact]
    public void TickFlag_With_AddsBits()
    {
        var flags = TickFlag.None.With(TickFlag.Last).With(TickFlag.Buy);

        Assert.True(flags.HasAll(TickFlag.Last | TickFlag.Buy));
        Assert.Equal((int)(TickFlag.Last | TickFlag.Buy), (int)flags);
    }

    [Fact]
    public void TickFlag_Values_MatchLegacyConstants()
    {
        // Числовые значения должны совпадать с MQL5/JSON-контрактом (2/4/8/16/32/64).
        Assert.Equal(2, (int)TickFlag.Bid);
        Assert.Equal(4, (int)TickFlag.Ask);
        Assert.Equal(8, (int)TickFlag.Last);
        Assert.Equal(16, (int)TickFlag.Volume);
        Assert.Equal(32, (int)TickFlag.Buy);
        Assert.Equal(64, (int)TickFlag.Sell);
    }

    // --- TradeSide ---

    [Theory]
    [InlineData(TradeSide.Buy, TickFlag.Buy)]
    [InlineData(TradeSide.Sell, TickFlag.Sell)]
    public void TradeSide_ToFlag_MapsToDirection(TradeSide side, TickFlag expected)
    {
        Assert.Equal(expected, side.ToFlag());
    }

    [Fact]
    public void TickFlag_ForTrade_BuildsExpectedFlags()
    {
        var buyFlags = TickFlagExtensions.ForTrade(TradeSide.Buy);
        Assert.True(buyFlags.HasAll(TickFlag.Last | TickFlag.Volume | TickFlag.Buy));

        var sellFlags = TickFlagExtensions.ForTrade(TradeSide.Sell, changedVolume: false);
        Assert.True(sellFlags.HasAll(TickFlag.Last | TickFlag.Sell));
        Assert.False(sellFlags.HasAny(TickFlag.Volume));
    }

    [Fact]
    public void MyTick_FlagsEnum_MirrorsIntFlags()
    {
        var tick = new MyTick { Flags = (int)(TickFlag.Last | TickFlag.Sell) };

        Assert.True(tick.FlagsEnum.HasAll(TickFlag.Last | TickFlag.Sell));
        Assert.Equal(false, tick.IsBuy);
    }

    // --- JSON-совместимость (контракт flags остаётся целым числом) ---

    [Fact]
    public void MyTick_Json_FlagsSerializedAsNumber()
    {
        var tick = new MyTick { Flags = (int)TickFlag.Buy, Last = 1.0, TimeMsc = 1000 };

        var json = JsonSerializer.Serialize(tick);

        Assert.Contains("\"flags\":32", json);
    }

    [Fact]
    public void MyTick_Json_FlagsRoundTrip()
    {
        var original = new MyTick { Flags = (int)(TickFlag.Last | TickFlag.Sell), Last = 1.0, TimeMsc = 1000 };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<MyTick>(json);

        Assert.NotNull(restored);
        Assert.Equal(original.Flags, restored!.Flags);
        Assert.True(restored.FlagsEnum.HasAll(TickFlag.Last | TickFlag.Sell));
    }

    [Fact]
    public void MyTick_Json_FlagsAcceptsFloatNotation()
    {
        // Python сериализует числа без различия int/float (например, 32.0).
        var json = "{\"flags\":32.0,\"Last\":1.0,\"time_msc\":1000}";

        var tick = JsonSerializer.Deserialize<MyTick>(json);

        Assert.NotNull(tick);
        Assert.Equal((int)TickFlag.Buy, tick!.Flags);
    }
}
