using IQIAIndicator.Backtest.Cost;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Cost;

/// <summary>
/// Sprint 15.25 (Lot 14.7, brief §9). <see cref="ExecutionCostConfiguration"/> - the mandatory
/// "default = Lot 14.6 behaviour" property and the activation/deactivation switch.
/// </summary>
public sealed class ExecutionCostConfigurationTests
{
    // ── §9: default configuration reproduces Lot 14.6 exactly ──────────────────────────────────────

    [Fact]
    public void Disabled_IsEnabledFalse_WithEveryComponentAtZero()
    {
        ExecutionCostConfiguration config = ExecutionCostConfiguration.Disabled();

        Assert.False(config.Enabled);
        Assert.Equal(0m, config.Slippage.PriceUnits);
        Assert.Equal(0m, config.Spread.PriceUnits);
        Assert.Equal(0m, config.Commission.PerOrder);
        Assert.Equal(0m, config.Commission.PerUnit);
        Assert.Equal(0m, config.Fees.PerOrder);
    }

    [Fact]
    public void Create_OmittedComponents_DefaultToZeroEvenWhenEnabled()
    {
        ExecutionCostConfiguration config = ExecutionCostConfiguration.Create(enabled: true);

        Assert.True(config.Enabled);
        Assert.Equal(0m, config.Slippage.PriceUnits);
        Assert.Equal(0m, config.Spread.PriceUnits);
        Assert.Equal(0m, config.Commission.PerOrder);
        Assert.Equal(0m, config.Fees.PerOrder);
    }

    [Fact]
    public void Create_ExplicitComponents_AreUsedVerbatim()
    {
        ExecutionCostConfiguration config = ExecutionCostConfiguration.Create(
            enabled: true,
            slippage: SlippageConfiguration.Fixed(0.25m),
            spread: SpreadConfiguration.Fixed(0.5m),
            commission: CommissionConfiguration.Create(2m, 0.5m),
            fees: FeesConfiguration.Create(0.1m));

        Assert.Equal(0.25m, config.Slippage.PriceUnits);
        Assert.Equal(0.5m, config.Spread.PriceUnits);
        Assert.Equal(2m, config.Commission.PerOrder);
        Assert.Equal(0.5m, config.Commission.PerUnit);
        Assert.Equal(0.1m, config.Fees.PerOrder);
    }
}
