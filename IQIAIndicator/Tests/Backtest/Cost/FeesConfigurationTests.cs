using System;
using IQIAIndicator.Backtest.Cost;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Cost;

/// <summary>
/// Sprint 15.25 (Lot 14.7, brief §7/§12). <see cref="FeesConfiguration"/> - zero, fixed, and invalid
/// rejection. Kept strictly independent from <see cref="CommissionConfiguration"/> (brief §7).
/// </summary>
public sealed class FeesConfigurationTests
{
    [Fact]
    public void None_ProducesZero()
    {
        Assert.Equal(0m, FeesConfiguration.None().PerOrder);
    }

    [Fact]
    public void Create_StoresTheExactConfiguredValue()
    {
        Assert.Equal(0.10m, FeesConfiguration.Create(0.10m).PerOrder);
    }

    [Fact]
    public void Create_DefaultsToZeroWhenOmitted()
    {
        Assert.Equal(0m, FeesConfiguration.Create().PerOrder);
    }

    [Fact]
    public void Create_RejectsNegativeFees()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FeesConfiguration.Create(-0.01m));
    }
}
