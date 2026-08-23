using System;
using IQIAIndicator.Backtest.Cost;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Cost;

/// <summary>
/// Sprint 15.25 (Lot 14.7, brief §7/§12). <see cref="CommissionConfiguration"/> - zero, fixed-per-order,
/// per-unit, and invalid-configuration rejection. Applying both components to entry+exit orders is
/// <see cref="PositionCostCalculator"/>'s responsibility - see <c>PositionCostCalculatorTests</c>.
/// </summary>
public sealed class CommissionConfigurationTests
{
    [Fact]
    public void None_ProducesZeroForBothComponents()
    {
        CommissionConfiguration commission = CommissionConfiguration.None();
        Assert.Equal(0m, commission.PerOrder);
        Assert.Equal(0m, commission.PerUnit);
    }

    [Fact]
    public void Create_StoresBothComponentsIndependently()
    {
        CommissionConfiguration commission = CommissionConfiguration.Create(perOrder: 2.00m, perUnit: 0.50m);
        Assert.Equal(2.00m, commission.PerOrder);
        Assert.Equal(0.50m, commission.PerUnit);
    }

    [Fact]
    public void Create_DefaultsToZeroWhenOmitted()
    {
        CommissionConfiguration commission = CommissionConfiguration.Create();
        Assert.Equal(0m, commission.PerOrder);
        Assert.Equal(0m, commission.PerUnit);
    }

    [Fact]
    public void Create_RejectsNegativePerOrder()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CommissionConfiguration.Create(perOrder: -0.01m));
    }

    [Fact]
    public void Create_RejectsNegativePerUnit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CommissionConfiguration.Create(perUnit: -0.01m));
    }
}
