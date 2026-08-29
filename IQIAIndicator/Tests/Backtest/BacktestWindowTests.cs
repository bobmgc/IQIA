using System;
using IQIAIndicator.Backtest;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests;

/// <summary>Sprint 15.25 (Lot 14.1, brief §9). BacktestWindow is immutable and half-open [From, To).</summary>
public sealed class BacktestWindowTests
{
    [Fact]
    public void ValidRange_Creates()
    {
        var window = new BacktestWindow("TRAIN", new DateTime(2026, 1, 1), new DateTime(2026, 6, 1));
        Assert.Equal("TRAIN", window.Name);
    }

    [Fact]
    public void ToEqualToFrom_Throws()
    {
        var t = new DateTime(2026, 1, 1);
        Assert.Throws<ArgumentException>(() => new BacktestWindow("W", t, t));
    }

    [Fact]
    public void ToBeforeFrom_Throws()
    {
        Assert.Throws<ArgumentException>(() => new BacktestWindow("W", new DateTime(2026, 2, 1), new DateTime(2026, 1, 1)));
    }

    [Fact]
    public void EmptyName_Throws()
    {
        Assert.Throws<ArgumentException>(() => new BacktestWindow("", new DateTime(2026, 1, 1), new DateTime(2026, 2, 1)));
    }

    [Fact]
    public void Contains_IsInclusiveOfFrom_ExclusiveOfTo()
    {
        var window = new BacktestWindow("W", new DateTime(2026, 1, 1), new DateTime(2026, 1, 2));

        Assert.True(window.Contains(new DateTime(2026, 1, 1)));
        Assert.True(window.Contains(new DateTime(2026, 1, 1, 12, 0, 0)));
        Assert.False(window.Contains(new DateTime(2026, 1, 2)));
        Assert.False(window.Contains(new DateTime(2025, 12, 31)));
    }

    [Fact]
    public void AdjacentWindows_ShareNoInstant()
    {
        var first = new BacktestWindow("A", new DateTime(2026, 1, 1), new DateTime(2026, 2, 1));
        var second = new BacktestWindow("B", first.To, new DateTime(2026, 3, 1));

        Assert.False(first.Contains(second.From));
        Assert.True(second.Contains(second.From));
    }
}
