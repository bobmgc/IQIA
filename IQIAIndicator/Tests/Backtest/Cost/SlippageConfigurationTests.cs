using System;
using IQIAIndicator.Backtest.Cost;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Cost;

/// <summary>
/// Sprint 15.25 (Lot 14.7, brief §5/§11/§12). <see cref="SlippageConfiguration"/> - the three construction
/// paths (None/Fixed/FromTicks), determinism, and invalid-configuration rejection.
/// </summary>
public sealed class SlippageConfigurationTests
{
    // ── §5: "Aucun slippage" ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void None_ProducesZeroPriceUnits()
    {
        Assert.Equal(0m, SlippageConfiguration.None().PriceUnits);
    }

    // ── §5: "Slippage fixe" ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Fixed_StoresTheExactConfiguredValue()
    {
        Assert.Equal(0.25m, SlippageConfiguration.Fixed(0.25m).PriceUnits);
    }

    [Fact]
    public void Fixed_RejectsNegativeSlippage()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SlippageConfiguration.Fixed(-0.01m));
    }

    // ── §5: "Slippage en points/ticks" ───────────────────────────────────────────────────────────────

    [Fact]
    public void FromTicks_ConvertsTicksToPriceUnitsUsingTheInstrumentsTickSize()
    {
        // MES: TickSize = 0.25 -> 2 ticks = 0.5 price units.
        Assert.Equal(0.5m, SlippageConfiguration.FromTicks(ticks: 2m, tickSize: 0.25m).PriceUnits);
    }

    [Fact]
    public void FromTicks_RejectsNegativeTicks()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SlippageConfiguration.FromTicks(-1m, 0.25m));
    }

    [Fact]
    public void FromTicks_RejectsNonPositiveTickSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SlippageConfiguration.FromTicks(1m, 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => SlippageConfiguration.FromTicks(1m, -0.25m));
    }

    // ── determinism ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SameInputs_ProduceTheExactSameConfiguration()
    {
        Assert.Equal(SlippageConfiguration.FromTicks(3m, 0.25m), SlippageConfiguration.FromTicks(3m, 0.25m));
    }
}
