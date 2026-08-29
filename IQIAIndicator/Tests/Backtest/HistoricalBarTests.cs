using System;
using System.Linq;
using IQIAIndicator.Core.MarketData;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests;

/// <summary>Sprint 15.25 (Lot 14.1, brief §18). Structural validation of a single HistoricalBar - never
/// silently repaired, always detectable via Validate()/IsValid.</summary>
public sealed class HistoricalBarTests
{
    private static HistoricalBar ValidBar() => new(
        Timestamp: new DateTime(2026, 1, 5, 14, 30, 0, DateTimeKind.Utc),
        Open: 100m, High: 101m, Low: 99m, Close: 100.5m, Volume: 10m);

    [Fact]
    public void ValidBar_IsValid()
    {
        Assert.True(ValidBar().IsValid);
        Assert.Empty(ValidBar().Validate());
    }

    [Fact]
    public void DefaultTimestamp_IsInvalid()
    {
        var bar = ValidBar() with { Timestamp = default };
        Assert.False(bar.IsValid);
        Assert.Contains(bar.Validate(), e => e.Contains("Timestamp", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveOpen_IsInvalid(decimal open)
    {
        var bar = ValidBar() with { Open = open };
        Assert.False(bar.IsValid);
        Assert.Contains(bar.Validate(), e => e.Contains("Open", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveHigh_IsInvalid(decimal high)
    {
        var bar = ValidBar() with { High = high };
        Assert.False(bar.IsValid);
        Assert.Contains(bar.Validate(), e => e.Contains("High", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveLow_IsInvalid(decimal low)
    {
        var bar = ValidBar() with { Low = low };
        Assert.False(bar.IsValid);
        Assert.Contains(bar.Validate(), e => e.Contains("Low", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveClose_IsInvalid(decimal close)
    {
        var bar = ValidBar() with { Close = close };
        Assert.False(bar.IsValid);
        Assert.Contains(bar.Validate(), e => e.Contains("Close", StringComparison.Ordinal));
    }

    [Fact]
    public void HighBelowLow_IsInvalid()
    {
        var bar = ValidBar() with { High = 98m, Low = 99m };
        Assert.False(bar.IsValid);
        Assert.Contains(bar.Validate(), e => e.Contains("High", StringComparison.Ordinal) && e.Contains("Low", StringComparison.Ordinal));
    }

    [Fact]
    public void NegativeVolume_IsInvalid()
    {
        var bar = ValidBar() with { Volume = -1m };
        Assert.False(bar.IsValid);
        Assert.Contains(bar.Validate(), e => e.Contains("Volume", StringComparison.Ordinal));
    }

    [Fact]
    public void MultipleViolations_AreAllReported_NotJustTheFirst()
    {
        var bar = ValidBar() with { Open = 0m, Volume = -1m };
        var errors = bar.Validate();
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void HighLessThanCloseOrLowGreaterThanClose_IsStillValid_MatchesMarketContextValidatorWarningOnlyRule()
    {
        // MarketContextValidator.CheckPrice treats High<Close / Low>Close as WARNINGS, not errors - this
        // type must not be stricter than the pipeline it feeds (see HistoricalBar.Validate's doc comment).
        var bar = ValidBar() with { High = 100.2m, Close = 100.5m };
        Assert.True(bar.IsValid);
    }

    [Fact]
    public void AbsentMicrostructureFields_StayNull_NeverZeroFilled()
    {
        HistoricalBar bar = ValidBar();
        Assert.Null(bar.BidVolume);
        Assert.Null(bar.AskVolume);
        Assert.Null(bar.Delta);
        Assert.Null(bar.OpenInterest);
    }
}
