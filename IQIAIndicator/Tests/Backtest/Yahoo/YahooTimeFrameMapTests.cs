using System;
using IQIAIndicator.Core.MarketData.Yahoo;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Yahoo;

/// <summary>Sprint 15.25 (Lot 14.2, brief §8/§19). Only "M5" is supported this lot; every other
/// TimeFrame must be explicitly rejected, never silently mapped to a different interval.</summary>
public sealed class YahooTimeFrameMapTests
{
    [Fact]
    public void M5_ResolvesTo5m()
    {
        Assert.Equal("5m", YahooTimeFrameMap.ResolveYahooInterval("M5"));
    }

    [Theory]
    [InlineData("M1")]
    [InlineData("M15")]
    [InlineData("H1")]
    [InlineData("D1")]
    [InlineData("")]
    [InlineData("m5")] // ordinal-cased: lowercase is not the same key
    public void UnsupportedTimeFrame_ThrowsRatherThanFallingBackToADifferentInterval(string timeFrame)
    {
        Assert.ThrowsAny<Exception>(() => YahooTimeFrameMap.ResolveYahooInterval(timeFrame));
    }

    [Fact]
    public void SupportedTimeFrames_ContainsOnlyM5_InThisLot()
    {
        var supported = YahooTimeFrameMap.SupportedTimeFrames;
        Assert.Single(supported);
        Assert.Contains("M5", supported);
    }
}
