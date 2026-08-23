using System;
using IQIAIndicator.Backtest.Risk;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Risk;

/// <summary>
/// Sprint 15.25 (Lot 14.8, brief §8/§17). <see cref="RiskDistanceConfiguration"/> - construction paths and
/// invalid-configuration rejection. The "no stop -&gt; not calculable" semantics are
/// <see cref="PositionRiskEvaluator"/>'s responsibility - see <c>PositionRiskEvaluatorTests</c>.
/// </summary>
public sealed class RiskDistanceConfigurationTests
{
    [Fact]
    public void None_ProducesZeroPriceUnits()
    {
        Assert.Equal(0m, RiskDistanceConfiguration.None().PriceUnits);
    }

    [Fact]
    public void Fixed_StoresTheExactConfiguredValue()
    {
        Assert.Equal(2m, RiskDistanceConfiguration.Fixed(2m).PriceUnits);
    }

    [Fact]
    public void Fixed_ZeroIsAccepted_ButMeansNoUsableDistance()
    {
        Assert.Equal(0m, RiskDistanceConfiguration.Fixed(0m).PriceUnits);
    }

    [Fact]
    public void Fixed_RejectsNegativeDistance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RiskDistanceConfiguration.Fixed(-2m));
    }

    [Fact]
    public void FromTicks_ConvertsTicksToPriceUnitsUsingTheInstrumentsTickSize()
    {
        Assert.Equal(1m, RiskDistanceConfiguration.FromTicks(ticks: 4m, tickSize: 0.25m).PriceUnits);
    }

    [Fact]
    public void FromTicks_RejectsNegativeTicks()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RiskDistanceConfiguration.FromTicks(-1m, 0.25m));
    }

    [Fact]
    public void FromTicks_RejectsNonPositiveTickSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RiskDistanceConfiguration.FromTicks(1m, 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => RiskDistanceConfiguration.FromTicks(1m, -0.25m));
    }
}
