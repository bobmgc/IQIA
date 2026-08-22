using System;
using System.Collections.Generic;
using IQIAIndicator.Core.MarketData;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests;

/// <summary>Sprint 15.25 (Lot 14.1, brief §4/§18). HistoricalSeries never sorts, de-duplicates, or
/// repairs a defective input - every violation must be reported, never silently corrected.</summary>
public sealed class HistoricalSeriesTests
{
    private static HistoricalBar Bar(DateTime timestamp, decimal close = 100m) =>
        new(timestamp, Open: close, High: close + 1m, Low: close - 1m, Close: close, Volume: 5m);

    private static readonly DateTime T0 = new(2026, 1, 5, 14, 30, 0, DateTimeKind.Utc);

    private static List<HistoricalBar> ThreeAscendingBars() => new()
    {
        Bar(T0),
        Bar(T0.AddMinutes(5), 101m),
        Bar(T0.AddMinutes(10), 102m)
    };

    [Fact]
    public void ValidInput_Creates_PreservingOrderAndCount()
    {
        HistoricalSeries series = HistoricalSeries.Create("ES", "M5", "UTC", "TestProvider", ThreeAscendingBars());

        Assert.Equal(3, series.Count);
        Assert.Equal(T0, series.FirstTimestamp);
        Assert.Equal(T0.AddMinutes(10), series.LastTimestamp);
        Assert.Equal("ES", series.Symbol);
        Assert.Equal("M5", series.TimeFrame);
        Assert.Equal("UTC", series.TimeZone);
        Assert.Equal("TestProvider", series.Provider);
    }

    [Fact]
    public void EmptySeries_Rejected()
    {
        bool ok = HistoricalSeries.TryCreate("ES", "M5", "UTC", "P", Array.Empty<HistoricalBar>(), out _, out var errors);
        Assert.False(ok);
        Assert.Contains(errors, e => e.Contains("At least one bar", StringComparison.Ordinal));
    }

    [Fact]
    public void EmptySymbol_Rejected()
    {
        bool ok = HistoricalSeries.TryCreate("", "M5", "UTC", "P", ThreeAscendingBars(), out _, out var errors);
        Assert.False(ok);
        Assert.Contains(errors, e => e.Contains("Symbol", StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyTimeFrame_Rejected()
    {
        bool ok = HistoricalSeries.TryCreate("ES", "  ", "UTC", "P", ThreeAscendingBars(), out _, out var errors);
        Assert.False(ok);
        Assert.Contains(errors, e => e.Contains("TimeFrame", StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyTimeZone_Rejected_NeverDefaulted()
    {
        bool ok = HistoricalSeries.TryCreate("ES", "M5", "", "P", ThreeAscendingBars(), out _, out var errors);
        Assert.False(ok);
        Assert.Contains(errors, e => e.Contains("TimeZone", StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyProvider_Rejected()
    {
        bool ok = HistoricalSeries.TryCreate("ES", "M5", "UTC", "", ThreeAscendingBars(), out _, out var errors);
        Assert.False(ok);
        Assert.Contains(errors, e => e.Contains("Provider", StringComparison.Ordinal));
    }

    [Fact]
    public void NonIncreasingTimestamps_Rejected_NeverSortedSilently()
    {
        var bars = new List<HistoricalBar> { Bar(T0.AddMinutes(10)), Bar(T0), Bar(T0.AddMinutes(5)) };

        bool ok = HistoricalSeries.TryCreate("ES", "M5", "UTC", "P", bars, out HistoricalSeries series, out var errors);

        Assert.False(ok);
        Assert.Contains(errors, e => e.Contains("earlier than", StringComparison.Ordinal));
        // Bars are never reordered as a side effect of a failed validation.
        Assert.Null(series);
    }

    [Fact]
    public void DuplicateTimestamp_Rejected_AsDistinctFromOutOfOrder()
    {
        var bars = new List<HistoricalBar> { Bar(T0), Bar(T0), Bar(T0.AddMinutes(5)) };

        bool ok = HistoricalSeries.TryCreate("ES", "M5", "UTC", "P", bars, out _, out var errors);

        Assert.False(ok);
        Assert.Contains(errors, e => e.Contains("duplicate timestamp", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidBar_Rejected_WithIndexNamed()
    {
        var bars = ThreeAscendingBars();
        bars[1] = bars[1] with { Close = 0m };

        bool ok = HistoricalSeries.TryCreate("ES", "M5", "UTC", "P", bars, out _, out var errors);

        Assert.False(ok);
        Assert.Contains(errors, e => e.Contains("Bar[1]", StringComparison.Ordinal));
    }

    [Fact]
    public void Create_Throws_NamingEveryViolation()
    {
        var bars = new List<HistoricalBar> { Bar(T0), Bar(T0) };
        var ex = Assert.Throws<ArgumentException>(() => HistoricalSeries.Create("", "", "", "", bars));
        Assert.Contains("Symbol", ex.Message, StringComparison.Ordinal);
        Assert.Contains("TimeFrame", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CallerListMutationAfterCreate_DoesNotAffectSeries_DefensiveCopy()
    {
        var bars = ThreeAscendingBars();
        HistoricalSeries series = HistoricalSeries.Create("ES", "M5", "UTC", "P", bars);

        bars.Add(Bar(T0.AddMinutes(999)));

        Assert.Equal(3, series.Count);
    }
}
