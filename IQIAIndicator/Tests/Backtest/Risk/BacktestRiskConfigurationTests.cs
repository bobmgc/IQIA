using System;
using IQIAIndicator.Backtest.Risk;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Risk;

/// <summary>
/// Sprint 15.25 (Lot 14.8, brief §4/§17). <see cref="BacktestRiskConfiguration"/> - the mandatory
/// "default = Lot 14.6/14.7 behaviour" property, and invalid-configuration rejection.
/// </summary>
public sealed class BacktestRiskConfigurationTests
{
    [Fact]
    public void Disabled_IsEnableRiskControlsFalse_WithNoDistanceAndUnlimitedExposure()
    {
        BacktestRiskConfiguration config = BacktestRiskConfiguration.Disabled();

        Assert.False(config.EnableRiskControls);
        Assert.Equal(0m, config.RiskDistance.PriceUnits);
        Assert.Null(config.MaxExposure);
    }

    [Fact]
    public void Create_OmittedComponents_DefaultToNoneAndUnlimitedEvenWhenEnabled()
    {
        BacktestRiskConfiguration config = BacktestRiskConfiguration.Create(enableRiskControls: true);

        Assert.True(config.EnableRiskControls);
        Assert.Equal(0m, config.RiskDistance.PriceUnits);
        Assert.Null(config.MaxExposure);
    }

    [Fact]
    public void Create_ExplicitComponents_AreUsedVerbatim()
    {
        BacktestRiskConfiguration config = BacktestRiskConfiguration.Create(
            enableRiskControls: true,
            riskDistance: RiskDistanceConfiguration.Fixed(2m),
            maxExposure: 50000m);

        Assert.Equal(2m, config.RiskDistance.PriceUnits);
        Assert.Equal(50000m, config.MaxExposure);
    }

    [Fact]
    public void Create_RejectsNonPositiveMaxExposure()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BacktestRiskConfiguration.Create(true, maxExposure: 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => BacktestRiskConfiguration.Create(true, maxExposure: -1m));
    }
}
