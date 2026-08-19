using System;
using ATAS.Indicators;
using IQIAIndicator.Core;
using Xunit;

namespace IQIAIndicator.Tests.CoreTests;

/// <summary>
/// Sprint 15.25 (Lot 12.12, Problem B). Coverage for MarketContextBuilder's self-healing instrument
/// refresh (TickSize/RefreshInstrument) - the fix for one of the two structurally-confirmed gaps that can
/// make the Risk Engine panel show "NOT AVAILABLE" forever on an otherwise active, connected chart (see
/// the Lot 12.12 report, Problem B). IQIAIndicator.cs only ever (re)constructs this builder once, at
/// bar==0; if ATAS's own InstrumentInfo.TickSize was not yet populated at that single moment, the OLD
/// code baked TickSize=0 into the builder for the rest of the session, and
/// MarketContextValidator.CheckInstrument would then reject EVERY subsequent bar forever (TickSize
/// invalide : 0) - freezing the entire downstream pipeline with no diagnostic trace, no matter how many
/// real bars ATAS later delivered. These tests exercise RefreshInstrument/TickSize directly - both are
/// pure, ATAS-independent members (the getBar delegate is never invoked by either), so no ATAS.Indicators
/// .IndicatorCandle instance needs to be constructed here; MarketContextBuilder.Build itself (which does
/// invoke getBar) remains a live-ATAS-only path, consistent with this project's existing convention for
/// code that requires a real IndicatorCandle.
/// </summary>
public sealed class MarketContextBuilderSelfHealingTests
{
    private static Func<int, IndicatorCandle> NeverCalledGetBar() =>
        _ => throw new InvalidOperationException("getBar must not be invoked by TickSize/RefreshInstrument - only by Build().");

    [Fact]
    public void TickSize_ReflectsConstructorValue()
    {
        var builder = new MarketContextBuilder(
            NeverCalledGetBar(), symbol: "MES", tickSize: 0.25m, tickValue: 1.25m, pointValue: 5m, decimals: 2, timeFrame: "M5");

        Assert.Equal(0.25m, builder.TickSize);
    }

    [Fact]
    public void TickSize_StaysZero_WhenConstructedWithoutIt()
    {
        // Reproduces the exact failure mode: ATAS's InstrumentInfo.TickSize not yet populated at the
        // single moment (bar==0) IQIAIndicator.cs originally (pre-Lot-12.12) constructed this builder.
        var builder = new MarketContextBuilder(
            NeverCalledGetBar(), symbol: string.Empty, tickSize: 0m, tickValue: 0m, pointValue: 0m, decimals: 2, timeFrame: "M5");

        Assert.Equal(0m, builder.TickSize);
    }

    [Fact]
    public void RefreshInstrument_UpdatesTickSize()
    {
        var builder = new MarketContextBuilder(
            NeverCalledGetBar(), symbol: string.Empty, tickSize: 0m, tickValue: 0m, pointValue: 0m, decimals: 2, timeFrame: "M5");
        Assert.Equal(0m, builder.TickSize);

        builder.RefreshInstrument(symbol: "MES", tickSize: 0.25m, tickValue: 1.25m, pointValue: 5m, decimals: 2, timeFrame: "M5");

        Assert.Equal(0.25m, builder.TickSize);
    }

    [Fact]
    public void RefreshInstrument_CalledRepeatedly_LastValueWins_NeverAccumulates()
    {
        // Sanity check: RefreshInstrument must be a plain overwrite, never additive/accumulating -
        // matches CreateBuilder()'s own one-shot read-and-store semantics, just re-appliable.
        var builder = new MarketContextBuilder(
            NeverCalledGetBar(), symbol: string.Empty, tickSize: 0m, tickValue: 0m, pointValue: 0m, decimals: 2, timeFrame: "M5");

        builder.RefreshInstrument("ES", 0.25m, 12.5m, 50m, 2, "M5");
        Assert.Equal(0.25m, builder.TickSize);

        builder.RefreshInstrument("MES", 0.25m, 1.25m, 5m, 2, "M5");
        Assert.Equal(0.25m, builder.TickSize);
    }

    /// <summary>Sprint 15.25 (Lot 12.12): the caller-side rebuild condition IQIAIndicator.cs applies
    /// (bar==0 -&gt; full CreateBuilder(); otherwise, self-heal only when the CACHED TickSize is still
    /// non-positive AND ATAS now reports a valid one) - re-derived here as a pure predicate so its truth
    /// table is independently provable without an ATAS host. IQIAIndicator.cs's own condition must match
    /// this exactly (see IQIAIndicator.cs's OnCalculate, "else if" branch).</summary>
    private static bool ShouldSelfHeal(decimal cachedTickSize, decimal? freshTickSize) =>
        cachedTickSize <= 0m && freshTickSize is decimal fresh && fresh > 0m;

    [Theory]
    [InlineData(0.0, null, false)]      // still nothing from ATAS - do not touch the builder
    [InlineData(0.0, 0.0, false)]       // ATAS reports 0 too - still nothing to heal
    [InlineData(0.0, 0.25, true)]       // exactly the failure mode this fix targets
    [InlineData(0.25, 0.5, false)]      // already healthy - never overwrite a working builder mid-session
    public void ShouldSelfHeal_TruthTable(double cachedTickSize, double? freshTickSize, bool expected)
    {
        bool result = ShouldSelfHeal((decimal)cachedTickSize, freshTickSize is double f ? (decimal)f : null);
        Assert.Equal(expected, result);
    }
}
