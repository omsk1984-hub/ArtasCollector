using ArtasCore.Models;
using Stat.Application;
using Xunit;

namespace Stat.Tests.Application;

/// <summary>
/// Тесты переноса Python <c>StatCursor</c>: инициализация цен и логика check.
/// </summary>
public sealed class StatCursorTests
{
    private static StatCursor CreateCursor(int stopPoint = 3, int takePoint = 20) =>
        new("CNYRUBF", digits: 3, point: 0.001, stopPoint: stopPoint, takePoint: takePoint);

    [Fact]
    public void Init_Buy_ComputesPrices()
    {
        var cursor = CreateCursor();

        var stat = cursor.Init(pickPrice: 12.345, hasBuy: true);

        Assert.True(stat.HasBuy);
        Assert.Equal(12.346, stat.PriceOpen, 6);
        Assert.Equal(12.343, stat.PriceStop, 6);
        Assert.Equal(12.366, stat.PriceTake, 6);
    }

    [Fact]
    public void Init_Sell_ComputesPricesWithInvertedSign()
    {
        var cursor = CreateCursor();

        var stat = cursor.Init(pickPrice: 12.345, hasBuy: false);

        Assert.False(stat.HasBuy);
        Assert.Equal(12.344, stat.PriceOpen, 6);
        Assert.Equal(12.347, stat.PriceStop, 6);
        Assert.Equal(12.324, stat.PriceTake, 6);
    }

    [Fact]
    public void Init_RoundsToEven()
    {
        var cursor = new StatCursor("X", digits: 2, point: 0.005, stopPoint: 1, takePoint: 1);

        // 10.005 + 0.005 = 10.010 -> округление до 2 знаков.
        var stat = cursor.Init(pickPrice: 10.005, hasBuy: true);

        Assert.Equal(10.01, stat.PriceOpen, 6);
    }

    [Fact]
    public void Check_Buy_OpensWhenPriceBelowOpen()
    {
        var cursor = CreateCursor();
        var stat = cursor.Init(pickPrice: 12.345, hasBuy: true); // open=12.346, stop=12.343

        // Цена ниже открытия, но выше стопа -> сделка просто открывается.
        var fractal = new Fractal { Price = 12.345 };
        var (updated, isStopped) = cursor.Check(stat, fractal, index: 1);

        Assert.False(isStopped);
        Assert.Equal(1, updated.IndexOpen);
        Assert.Equal(1, updated.IndexTotal);
        Assert.True(updated.IsOpen);
    }

    [Fact]
    public void Check_Buy_StopsOnStop()
    {
        var cursor = CreateCursor();
        var stat = cursor.Init(pickPrice: 12.345, hasBuy: true); // open=12.346, stop=12.343
        stat = stat with { IndexOpen = 1 };

        var fractal = new Fractal { Price = 12.343 };
        var (updated, isStopped) = cursor.Check(stat, fractal, index: 2);

        Assert.True(isStopped);
        Assert.Equal(2, updated.IndexStop);
        Assert.Equal(4, updated.ResultStop); // stop_point + 1
        Assert.True(updated.IsStop);
    }

    [Fact]
    public void Check_Buy_TakesOnTake()
    {
        var cursor = CreateCursor();
        var stat = cursor.Init(pickPrice: 12.345, hasBuy: true); // open=12.346, take=12.366
        stat = stat with { IndexOpen = 1 };

        var fractal = new Fractal { Price = 12.366 };
        var (updated, isStopped) = cursor.Check(stat, fractal, index: 3);

        Assert.True(isStopped);
        Assert.Equal(3, updated.IndexTake);
        Assert.Equal(20, updated.ResultTake);
        Assert.True(updated.IsTake);
    }

    [Fact]
    public void Check_Buy_UpdatesResultTakeWhileOpen()
    {
        // point=1.0 — исключает влияние ошибок округления double на int-приведение.
        var cursor = new StatCursor("X", digits: 3, point: 1.0, stopPoint: 3, takePoint: 20);
        var stat = cursor.Init(pickPrice: 100.0, hasBuy: true); // open=101, stop=98, take=121
        stat = stat with { IndexOpen = 1 };

        var fractal = new Fractal { Price = 105.0 };
        var (updated, isStopped) = cursor.Check(stat, fractal, index: 2);

        Assert.False(isStopped);
        // (105 - 101) / 1 = 4
        Assert.Equal(4, updated.ResultTake);
    }

    [Fact]
    public void Check_Sell_StopsWhenPriceAboveStop()
    {
        var cursor = CreateCursor();
        var stat = cursor.Init(pickPrice: 12.345, hasBuy: false); // open=12.344, stop=12.347
        stat = stat with { IndexOpen = 1 };

        var fractal = new Fractal { Price = 12.347 };
        var (updated, isStopped) = cursor.Check(stat, fractal, index: 2);

        Assert.True(isStopped);
        Assert.Equal(2, updated.IndexStop);
        Assert.Equal(4, updated.ResultStop);
    }

    [Fact]
    public void Check_Sell_TakesWhenPriceBelowTake()
    {
        var cursor = CreateCursor();
        var stat = cursor.Init(pickPrice: 12.345, hasBuy: false); // open=12.344, take=12.324
        stat = stat with { IndexOpen = 1 };

        var fractal = new Fractal { Price = 12.324 };
        var (updated, isStopped) = cursor.Check(stat, fractal, index: 3);

        Assert.True(isStopped);
        Assert.Equal(3, updated.IndexTake);
        Assert.Equal(20, updated.ResultTake);
    }

    [Fact]
    public void Check_AlwaysUpdatesIndexTotal()
    {
        var cursor = CreateCursor();
        var stat = cursor.Init(pickPrice: 12.345, hasBuy: true);

        var fractal = new Fractal { Price = 12.400 }; // выше open, не открывается
        var (updated, isStopped) = cursor.Check(stat, fractal, index: 5);

        Assert.False(isStopped);
        Assert.Equal(5, updated.IndexTotal);
        Assert.False(updated.IsOpen);
    }
}
