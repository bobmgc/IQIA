using System;
using System.Collections.Generic;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.EntryTrigger;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Execution;

/// <summary>
/// Sprint 15.25 (Lot 15.5, brief §7-13, Option D). Golden, hand-crafted dataset for the fill-price
/// reconciliation <see cref="ExecutionSimulator.SimulateCore"/> gained this lot - never Yahoo data
/// (deterministic by construction). Follows the exact hand-built-bars style used by
/// <see cref="ExecutionIntrabarGoldenDatasetTests"/> (Lot 15.4): bar 0 = signal bar (never read for
/// price), bar 1 = FILL bar (its Open is the real EntryPrice), the intrabar monitoring window starts at
/// the fill bar itself and runs through the horizon bar.
///
/// OBSERVABILITY NOTE: <see cref="SimulatedPosition"/> does not expose the reconciled
/// stopLossForExecution/takeProfitForExecution levels directly (they are a private derivation inside
/// SimulateCore, by design - see the Lot 15.5 comment block in ExecutionSimulator.cs). Every test below
/// therefore constructs the monitored bar's High/Low to touch the EXPECTED reconciled level exactly, so
/// the resulting <see cref="SimulatedPosition.ExitPrice"/> reveals that level precisely (the same technique
/// Lot 15.4's own golden dataset already relies on for the un-reconciled case).
///
/// Every candidate is constructed directly (never via <see cref="ExecutionCandidate.FromSignal"/>) so
/// referencePrice/StopLoss/TakeProfit are exactly the values each test intends - no TradePlanBuilder/
/// VolatilityStopLossModel involvement, no calibration of any kind.
/// </summary>
public sealed class FillPriceReconciliationGoldenTests
{
    private static readonly DateTime Anchor = new(2026, 1, 5, 14, 30, 0, DateTimeKind.Utc);

    private static HistoricalBar Bar(int minutesFromAnchor, decimal open, decimal high, decimal low, decimal close) =>
        new(Anchor.AddMinutes(minutesFromAnchor), open, high, low, close, Volume: 100m);

    private static HistoricalBar Flat(int minutesFromAnchor, decimal price) =>
        Bar(minutesFromAnchor, price, price, price, price);

    /// <summary><paramref name="referencePrice"/> is candidate.EntryPrice - the SIGNAL bar's reference
    /// price (TradePlan.EntryPrice), never the real fill.</summary>
    private static ExecutionCandidate Candidate(
        int signalBarIndex, DirectionCandidate direction, decimal? referencePrice,
        decimal? stopLoss = null, decimal? takeProfit = null) =>
        new(signalBarIndex, Anchor.AddMinutes(signalBarIndex * 5), direction, referencePrice, null, stopLoss, takeProfit);

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // §1: zero-move - referencePrice == real fill exactly. Reconciled levels must be BIT-IDENTICAL to
    // the original TradePlan.StopLoss/.TakeProfit (brief §10, backward-compatible reference case).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Buy_ZeroMove_ReconciledStopLossIsBitIdenticalToOriginalTradePlanValue()
    {
        // reference=100, fill=100 (zero move). StopLoss=95 (distance=5) -> reconciled = 100-5 = 95, i.e.
        // the ORIGINAL value, unchanged.
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Flat(5, 100m),                     // fill bar: entryPrice=100=referencePrice
            Bar(10, 100m, 101m, 95m, 97m),     // exit bar: Low=95 touches the (unchanged) StopLoss only
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 95m, takeProfit: 105m), ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(ExitReason.StopLoss, position.ExitReason);
        Assert.Equal(95m, position.ExitPrice); // == original TradePlan.StopLoss, bit-identical
    }

    [Fact]
    public void Sell_ZeroMove_ReconciledStopLossIsBitIdenticalToOriginalTradePlanValue()
    {
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Flat(5, 100m),
            Bar(10, 100m, 105m, 99m, 102m),    // High=105 touches the (unchanged) StopLoss only
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.SELL_CANDIDATE, 100m, stopLoss: 105m, takeProfit: 95m), ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(ExitReason.StopLoss, position.ExitReason);
        Assert.Equal(105m, position.ExitPrice);
    }

    [Fact]
    public void Buy_ZeroMove_ReconciledTakeProfitIsBitIdenticalToOriginalTradePlanValue()
    {
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Flat(5, 100m),
            Bar(10, 100m, 105m, 99m, 102m),    // High=105 touches the (unchanged) TakeProfit only
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 95m, takeProfit: 105m), ExecutionConfiguration.Create(1));

        Assert.Equal(ExitReason.TakeProfit, position.ExitReason);
        Assert.Equal(105m, position.ExitPrice);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // §2: positive delta - price moved between signal and fill. Reconciled levels are correctly sided
    // and the distance from the REAL fill equals the ORIGINAL distance from referencePrice.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Buy_PositiveDelta_FillAboveReference_ReconciledTakeProfitPreservesOriginalDistance()
    {
        // reference=100, fill=103. StopLoss=95 (distance=5) -> reconciled stop = 103-5=98.
        // TakeProfit=110 (distance=10) -> reconciled target = 103+10=113.
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Bar(5, 103m, 103m, 103m, 103m),        // fill bar: entryPrice=103
            Bar(10, 103m, 113m, 100m, 108m),       // High=113 touches reconciled target; Low=100 > reconciled stop (98)
        };
        ExecutionCandidate candidate = Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 95m, takeProfit: 110m);

        SimulatedPosition position = ExecutionSimulator.SimulateCore(bars, candidate, ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(ExitReason.TakeProfit, position.ExitReason);
        Assert.Equal(113m, position.ExitPrice);
        Assert.Equal(Math.Abs(110m - 100m), Math.Abs(position.ExitPrice!.Value - 103m)); // distance preserved: 10 == 10
    }

    [Fact]
    public void Sell_PositiveDelta_FillBelowReference_ReconciledTakeProfitPreservesOriginalDistance()
    {
        // reference=100, fill=97 (mirror of the BUY case above: price moved favorably for SELL).
        // StopLoss=105 (distance=5) -> reconciled stop = 97+5=102. TakeProfit=90 (distance=10) ->
        // reconciled target = 97-10=87.
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Bar(5, 97m, 97m, 97m, 97m),
            Bar(10, 97m, 95m, 87m, 90m),           // Low=87 touches reconciled target; High=95 < reconciled stop (102)
        };
        ExecutionCandidate candidate = Candidate(0, DirectionCandidate.SELL_CANDIDATE, 100m, stopLoss: 105m, takeProfit: 90m);

        SimulatedPosition position = ExecutionSimulator.SimulateCore(bars, candidate, ExecutionConfiguration.Create(1));

        Assert.Equal(ExitReason.TakeProfit, position.ExitReason);
        Assert.Equal(87m, position.ExitPrice);
        Assert.Equal(Math.Abs(90m - 100m), Math.Abs(position.ExitPrice!.Value - 97m)); // 10 == 10
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // §3: negative delta, SMALL - price moved unfavorably between signal and fill, but small enough that
    // Option A (Lot 15.4's raw comparison against the fill) would NOT have rejected it. Confirms
    // reconciliation still produces a sensible, non-degenerate result in the "would have passed anyway"
    // zone (not just in the rescue zone).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Buy_NegativeDelta_Small_WouldNotHaveBeenRejectedUnderOptionA_StillReconcilesSensibly()
    {
        // reference=100, fill=98. StopLoss=95 (distance=5) -> reconciled stop = 98-5=93.
        // Option A check (Lot 15.4): StopLoss(95) < fill(98) -> TRUE, would NOT have rejected.
        Assert.True(95m < 98m); // documents that Option A would have passed this case unchanged

        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Bar(5, 98m, 98m, 98m, 98m),
            Bar(10, 98m, 100m, 93m, 95m),          // Low=93 touches reconciled stop; High=100 < reconciled target (108)
        };
        ExecutionCandidate candidate = Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 95m, takeProfit: 110m);

        SimulatedPosition position = ExecutionSimulator.SimulateCore(bars, candidate, ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(ExitReason.StopLoss, position.ExitReason);
        Assert.Equal(93m, position.ExitPrice);
        Assert.True(position.ExitPrice > 0m); // non-degenerate
        Assert.Equal(5m, Math.Abs(95m - 100m)); // original distance
        Assert.Equal(5m, Math.Abs(position.ExitPrice!.Value - 98m)); // preserved onto the real fill
    }

    [Fact]
    public void Sell_NegativeDelta_Small_WouldNotHaveBeenRejectedUnderOptionA_StillReconcilesSensibly()
    {
        // reference=100, fill=102. StopLoss=105 (distance=5) -> reconciled stop = 102+5=107.
        // Option A check: StopLoss(105) > fill(102) -> TRUE, would NOT have rejected.
        Assert.True(105m > 102m);

        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Bar(5, 102m, 102m, 102m, 102m),
            Bar(10, 102m, 107m, 95m, 100m),        // High=107 touches reconciled stop; Low=95 > reconciled target (92)
        };
        ExecutionCandidate candidate = Candidate(0, DirectionCandidate.SELL_CANDIDATE, 100m, stopLoss: 105m, takeProfit: 90m);

        SimulatedPosition position = ExecutionSimulator.SimulateCore(bars, candidate, ExecutionConfiguration.Create(1));

        Assert.Equal(ExitReason.StopLoss, position.ExitReason);
        Assert.Equal(107m, position.ExitPrice);
        Assert.Equal(5m, Math.Abs(position.ExitPrice!.Value - 102m));
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // §4 (brief §9): gap scenarios exactly as specified - small (ref=100,fill=101/99), gap down
    // (ref=100,fill=95), gap up (ref=100,fill=105) - mirrored for BUY and SELL, each proving: correct
    // side of the real fill, and the reconciled distance equals the original signal-time distance.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Buy_SmallGap_Reference100_Fill101_DistancePreserved()
    {
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Bar(5, 101m, 101m, 101m, 101m),
            Bar(10, 101m, 105m, 96m, 99m),         // Low=96 touches reconciled stop; High=105 < reconciled target (111)
        };
        ExecutionCandidate candidate = Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 95m, takeProfit: 110m);

        SimulatedPosition position = ExecutionSimulator.SimulateCore(bars, candidate, ExecutionConfiguration.Create(1));

        Assert.Equal(ExitReason.StopLoss, position.ExitReason);
        Assert.Equal(96m, position.ExitPrice);
        Assert.True(position.ExitPrice < 101m); // correct side of the real fill
        Assert.Equal(5m, Math.Abs(95m - 100m));
        Assert.Equal(5m, Math.Abs(position.ExitPrice!.Value - 101m));
    }

    [Fact]
    public void Sell_SmallGap_Reference100_Fill99_DistancePreserved()
    {
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Bar(5, 99m, 99m, 99m, 99m),
            Bar(10, 99m, 104m, 95m, 100m),         // High=104 touches reconciled stop; Low=95 > reconciled target (89)
        };
        ExecutionCandidate candidate = Candidate(0, DirectionCandidate.SELL_CANDIDATE, 100m, stopLoss: 105m, takeProfit: 90m);

        SimulatedPosition position = ExecutionSimulator.SimulateCore(bars, candidate, ExecutionConfiguration.Create(1));

        Assert.Equal(ExitReason.StopLoss, position.ExitReason);
        Assert.Equal(104m, position.ExitPrice);
        Assert.True(position.ExitPrice > 99m);
        Assert.Equal(5m, Math.Abs(position.ExitPrice!.Value - 99m));
    }

    [Fact]
    public void Buy_GapDown_Reference100_Fill95_GeneralCase_NotTheDeliberateRejectionCase()
    {
        // General gap-down characterization (brief: "BUY gap down reference=100, fill=95"), using a
        // StopLoss/TakeProfit pair that Option A would ALSO have accepted (StopLoss=90 < fill=95;
        // TakeProfit=120 > fill=95) - the deliberate would-have-been-rejected construction is its own
        // dedicated test below.
        Assert.True(90m < 95m);
        Assert.True(120m > 95m);

        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Bar(5, 95m, 95m, 95m, 95m),
            Bar(10, 95m, 100m, 85m, 90m),          // Low=85 touches reconciled stop; High=100 < reconciled target (115)
        };
        ExecutionCandidate candidate = Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 90m, takeProfit: 120m);

        SimulatedPosition position = ExecutionSimulator.SimulateCore(bars, candidate, ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(ExitReason.StopLoss, position.ExitReason);
        Assert.Equal(85m, position.ExitPrice);
        Assert.Equal(10m, Math.Abs(90m - 100m));
        Assert.Equal(10m, Math.Abs(position.ExitPrice!.Value - 95m));
    }

    [Fact]
    public void Buy_GapUp_Reference100_Fill105_GeneralCase()
    {
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Bar(5, 105m, 105m, 105m, 105m),
            Bar(10, 105m, 115m, 101m, 108m),       // High=115 touches reconciled target; Low=101 > reconciled stop (100)
        };
        ExecutionCandidate candidate = Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 95m, takeProfit: 110m);

        SimulatedPosition position = ExecutionSimulator.SimulateCore(bars, candidate, ExecutionConfiguration.Create(1));

        Assert.Equal(ExitReason.TakeProfit, position.ExitReason);
        Assert.Equal(115m, position.ExitPrice);
        Assert.Equal(10m, Math.Abs(110m - 100m));
        Assert.Equal(10m, Math.Abs(position.ExitPrice!.Value - 105m));
    }

    [Fact]
    public void Sell_GapDown_Reference100_Fill95_MirrorOfBuyGapUp()
    {
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Bar(5, 95m, 95m, 95m, 95m),
            Bar(10, 95m, 99m, 85m, 90m),           // Low=85 touches reconciled target; High=99 < reconciled stop (100)
        };
        ExecutionCandidate candidate = Candidate(0, DirectionCandidate.SELL_CANDIDATE, 100m, stopLoss: 105m, takeProfit: 90m);

        SimulatedPosition position = ExecutionSimulator.SimulateCore(bars, candidate, ExecutionConfiguration.Create(1));

        Assert.Equal(ExitReason.TakeProfit, position.ExitReason);
        Assert.Equal(85m, position.ExitPrice);
        Assert.Equal(10m, Math.Abs(position.ExitPrice!.Value - 95m));
    }

    [Fact]
    public void Sell_GapUp_Reference100_Fill105_MirrorOfBuyGapDown()
    {
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Bar(5, 105m, 105m, 105m, 105m),
            Bar(10, 105m, 115m, 90m, 108m),        // High=115 touches reconciled stop; Low=90 > reconciled target (85)
        };
        ExecutionCandidate candidate = Candidate(0, DirectionCandidate.SELL_CANDIDATE, 100m, stopLoss: 110m, takeProfit: 80m);

        SimulatedPosition position = ExecutionSimulator.SimulateCore(bars, candidate, ExecutionConfiguration.Create(1));

        Assert.Equal(ExitReason.StopLoss, position.ExitReason);
        Assert.Equal(115m, position.ExitPrice);
        Assert.Equal(10m, Math.Abs(position.ExitPrice!.Value - 105m));
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // §5: the DELIBERATE would-have-been-InvalidStopTarget-under-Option-A construction (BUY gap down
    // large enough that the original StopLoss > the real fill) - confirms it now resolves through
    // (Closed), NEVER InvalidStopTarget, under Lot 15.5's reconciliation. Mirrored for SELL.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Buy_GapDownLargeEnoughThatOriginalStopLossExceedsFill_OptionAWouldHaveRejected_Lot155ResolvesToClosed()
    {
        // reference=100, fill=95, StopLoss=97 (distance=3). Original StopLoss (97) IS on the correct side
        // of referencePrice (97 < 100) - passes the pre-reconciliation check. But 97 > fill(95): under
        // Option A (Lot 15.4's raw comparison against the fill) this would have been InvalidStopTarget.
        Assert.True(97m < 100m);   // passes pre-reconciliation check (valid at signal time)
        Assert.False(97m < 95m);   // Option A's BUY invariant (StopLoss < fill) is VIOLATED - would have rejected

        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Bar(5, 95m, 95m, 95m, 95m),             // fill bar: entryPrice=95
            Bar(10, 95m, 100m, 92m, 94m),           // Low=92 touches reconciled stop (95-3=92); High=100 < reconciled target (110)
        };
        ExecutionCandidate candidate = Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 97m, takeProfit: 115m);

        SimulatedPosition position = ExecutionSimulator.SimulateCore(bars, candidate, ExecutionConfiguration.Create(1));

        Assert.NotEqual(PositionStatus.InvalidStopTarget, position.Status);
        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(ExitReason.StopLoss, position.ExitReason);
        Assert.Equal(92m, position.ExitPrice);
        Assert.Equal(3m, Math.Abs(97m - 100m));
        Assert.Equal(3m, Math.Abs(position.ExitPrice!.Value - 95m));
    }

    [Fact]
    public void Sell_GapUpLargeEnoughThatOriginalStopLossExceedsFill_OptionAWouldHaveRejected_Lot155ResolvesToClosed()
    {
        // Mirror: reference=100, fill=105, StopLoss=103 (distance=3). 103 > 100 -> passes pre-check.
        // Option A's SELL invariant (StopLoss > fill) needs 103 > 105 -> FALSE -> would have rejected.
        Assert.True(103m > 100m);
        Assert.False(103m > 105m);

        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Bar(5, 105m, 105m, 105m, 105m),
            Bar(10, 105m, 108m, 95m, 100m),         // High=108 touches reconciled stop (105+3=108); Low=95 > reconciled target (90)
        };
        ExecutionCandidate candidate = Candidate(0, DirectionCandidate.SELL_CANDIDATE, 100m, stopLoss: 103m, takeProfit: 85m);

        SimulatedPosition position = ExecutionSimulator.SimulateCore(bars, candidate, ExecutionConfiguration.Create(1));

        Assert.NotEqual(PositionStatus.InvalidStopTarget, position.Status);
        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(ExitReason.StopLoss, position.ExitReason);
        Assert.Equal(108m, position.ExitPrice);
        Assert.Equal(3m, Math.Abs(position.ExitPrice!.Value - 105m));
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // §6 (brief §11): extreme move - a move large enough that under Option A the level would have been
    // massively on the wrong side. Confirms reconciliation still produces a well-formed, correctly-sided,
    // non-degenerate level. "Extreme" used here: a 50% drop/rise between signal and fill (100 -> 50 for
    // BUY, 100 -> 150 for SELL).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Buy_ExtremeMove_HalvedPrice_ReconciledLevelsAreWellFormedAndCorrectlySided()
    {
        // reference=100, fill=50 (extreme -50% move). StopLoss=90 (distance=10) -> reconciled=50-10=40.
        // TakeProfit=120 (distance=20) -> reconciled=50+20=70. Both well-formed: 40 < 50 < 70.
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Bar(5, 50m, 50m, 50m, 50m),
            Bar(10, 50m, 60m, 40m, 45m),           // Low=40 touches reconciled stop; High=60 < reconciled target (70)
        };
        ExecutionCandidate candidate = Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 90m, takeProfit: 120m);

        SimulatedPosition position = ExecutionSimulator.SimulateCore(bars, candidate, ExecutionConfiguration.Create(1));

        Assert.NotEqual(PositionStatus.InvalidStopTarget, position.Status);
        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(ExitReason.StopLoss, position.ExitReason);
        Assert.Equal(40m, position.ExitPrice);
        Assert.True(position.ExitPrice > 0m);       // non-degenerate
        Assert.True(position.ExitPrice < 50m);      // correct side of the real fill
        Assert.Equal(10m, Math.Abs(90m - 100m));
        Assert.Equal(10m, Math.Abs(position.ExitPrice!.Value - 50m));
    }

    [Fact]
    public void Sell_ExtremeMove_OneAndHalfPrice_ReconciledLevelsAreWellFormedAndCorrectlySided()
    {
        // reference=100, fill=150 (extreme +50% move). StopLoss=110 (distance=10) -> reconciled=150+10=160.
        // TakeProfit=80 (distance=20) -> reconciled=150-20=130. Well-formed: 130 < 150 < 160.
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Bar(5, 150m, 150m, 150m, 150m),
            Bar(10, 150m, 145m, 130m, 140m),       // Low=130 touches reconciled target; High=145 < reconciled stop (160)
        };
        ExecutionCandidate candidate = Candidate(0, DirectionCandidate.SELL_CANDIDATE, 100m, stopLoss: 110m, takeProfit: 80m);

        SimulatedPosition position = ExecutionSimulator.SimulateCore(bars, candidate, ExecutionConfiguration.Create(1));

        Assert.NotEqual(PositionStatus.InvalidStopTarget, position.Status);
        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(ExitReason.TakeProfit, position.ExitReason);
        Assert.Equal(130m, position.ExitPrice);
        Assert.True(position.ExitPrice > 0m);
        Assert.True(position.ExitPrice < 150m);
        Assert.Equal(20m, Math.Abs(80m - 100m));
        Assert.Equal(20m, Math.Abs(position.ExitPrice!.Value - 150m));
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // §7 (brief §5, the pre-reconciliation check): TradePlan.StopLoss/.TakeProfit malformed relative to
    // its OWN referencePrice (not just the fill) - InvalidStopTarget with the NEW "malformed before any
    // fill-price reconciliation" message. All 4 combinations: StopLoss/TakeProfit x BUY/SELL.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Buy_StopLossOnWrongSideOfOwnReferencePrice_IsInvalidStopTarget_WithMalformedAtSourceMessage()
    {
        var bars = new List<HistoricalBar> { Flat(0, 999m), Flat(5, 100m), Flat(10, 100m) };
        // BUY requires StopLoss < referencePrice; 105 is on the wrong side of referencePrice=100 itself
        // (not merely the fill - the fill here is a plain 100, unrelated to the violation).
        ExecutionCandidate candidate = Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 105m, takeProfit: 110m);

        SimulatedPosition position = ExecutionSimulator.SimulateCore(bars, candidate, ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.InvalidStopTarget, position.Status);
        Assert.NotEqual(PositionStatus.Closed, position.Status);
        Assert.Contains("malformed before any fill-price reconciliation", position.Reason);
        Assert.Contains("StopLoss", position.Reason);
    }

    [Fact]
    public void Buy_TakeProfitOnWrongSideOfOwnReferencePrice_IsInvalidStopTarget_WithMalformedAtSourceMessage()
    {
        var bars = new List<HistoricalBar> { Flat(0, 999m), Flat(5, 100m), Flat(10, 100m) };
        // BUY requires TakeProfit > referencePrice; 95 is on the wrong side of referencePrice=100 itself.
        ExecutionCandidate candidate = Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 90m, takeProfit: 95m);

        SimulatedPosition position = ExecutionSimulator.SimulateCore(bars, candidate, ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.InvalidStopTarget, position.Status);
        Assert.Contains("malformed before any fill-price reconciliation", position.Reason);
        Assert.Contains("TakeProfit", position.Reason);
    }

    [Fact]
    public void Sell_StopLossOnWrongSideOfOwnReferencePrice_IsInvalidStopTarget_WithMalformedAtSourceMessage()
    {
        var bars = new List<HistoricalBar> { Flat(0, 999m), Flat(5, 100m), Flat(10, 100m) };
        // SELL requires StopLoss > referencePrice; 95 is on the wrong side of referencePrice=100 itself.
        ExecutionCandidate candidate = Candidate(0, DirectionCandidate.SELL_CANDIDATE, 100m, stopLoss: 95m, takeProfit: 90m);

        SimulatedPosition position = ExecutionSimulator.SimulateCore(bars, candidate, ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.InvalidStopTarget, position.Status);
        Assert.Contains("malformed before any fill-price reconciliation", position.Reason);
        Assert.Contains("StopLoss", position.Reason);
    }

    [Fact]
    public void Sell_TakeProfitOnWrongSideOfOwnReferencePrice_IsInvalidStopTarget_WithMalformedAtSourceMessage()
    {
        var bars = new List<HistoricalBar> { Flat(0, 999m), Flat(5, 100m), Flat(10, 100m) };
        // SELL requires TakeProfit < referencePrice; 105 is on the wrong side of referencePrice=100 itself.
        ExecutionCandidate candidate = Candidate(0, DirectionCandidate.SELL_CANDIDATE, 100m, stopLoss: 110m, takeProfit: 105m);

        SimulatedPosition position = ExecutionSimulator.SimulateCore(bars, candidate, ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.InvalidStopTarget, position.Status);
        Assert.Contains("malformed before any fill-price reconciliation", position.Reason);
        Assert.Contains("TakeProfit", position.Reason);
    }
}
