using System;
using IQIAIndicator.Backtest.Cost;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Cost;

/// <summary>
/// Sprint 15.25 (Lot 14.7, brief §6/§11/§12). <see cref="SpreadConfiguration"/> - construction paths and
/// invalid-configuration rejection. The bid/ask application itself is <see cref="ExecutionPriceModel"/>'s
/// responsibility - see <c>ExecutionPriceModelTests</c>.
/// </summary>
public sealed class SpreadConfigurationTests
{
    [Fact]
    public void None_ProducesZeroPriceUnits()
    {
        Assert.Equal(0m, SpreadConfiguration.None().PriceUnits);
    }

    [Fact]
    public void Fixed_StoresTheExactConfiguredValue()
    {
        Assert.Equal(0.5m, SpreadConfiguration.Fixed(0.5m).PriceUnits);
    }

    [Fact]
    public void Fixed_RejectsNegativeSpread()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SpreadConfiguration.Fixed(-0.5m));
    }

    [Fact]
    public void FromTicks_ConvertsTicksToPriceUnitsUsingTheInstrumentsTickSize()
    {
        Assert.Equal(0.5m, SpreadConfiguration.FromTicks(ticks: 2m, tickSize: 0.25m).PriceUnits);
    }

    [Fact]
    public void FromTicks_RejectsNegativeTicks()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SpreadConfiguration.FromTicks(-1m, 0.25m));
    }

    [Fact]
    public void FromTicks_RejectsNonPositiveTickSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SpreadConfiguration.FromTicks(1m, 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpreadConfiguration.FromTicks(1m, -0.25m));
    }
}
