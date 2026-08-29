using System;
using System.Collections.Generic;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.EntryTrigger;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Execution;

/// <summary>
/// Sprint 15.25 (Lot 15.4, brief §27/§32, §14-§18). Golden, hand-crafted synthetic dataset proving the
/// intrabar Stop Loss / Take Profit monitoring <see cref="ExecutionSimulator.SimulateCore"/> gained this
/// lot - never Yahoo data (deterministic by construction). Follows the exact hand-built-bars style already
/// used by <see cref="ExecutionSimulatorFormulaTests"/> (bar 0 = signal bar, never read for price; bar 1 =
/// FILL bar, its Open is the real EntryPrice per the Lot 14.10 fill convention; exit bar sits
/// <c>HorizonBars</c> after the fill bar).
///
/// Every test below constructs its <see cref="ExecutionCandidate"/> directly (never via
/// <see cref="ExecutionCandidate.FromSignal"/>) so StopLoss/TakeProfit are exactly the values the test
/// intends - no TradePlanBuilder/VolatilityStopLossModel involvement, no calibration of any kind (hard
/// constraint: no tuning against PnL/win-rate/Sharpe anywhere in this file).
/// </summary>
public sealed class ExecutionIntrabarGoldenDatasetTests
{
    private static readonly DateTime Anchor = new(2026, 1, 5, 14, 30, 0, DateTimeKind.Utc);

    private static HistoricalBar Bar(int minutesFromAnchor, decimal open, decimal high, decimal low, decimal close) =>
        new(Anchor.AddMinutes(minutesFromAnchor), open, high, low, close, Volume: 100m);

    /// <summary>A flat bar - Open=High=Low=Close - for filler bars where only "does not touch either
    /// level" matters.</summary>
    private static HistoricalBar Flat(int minutesFromAnchor, decimal price) =>
        Bar(minutesFromAnchor, price, price, price, price);

    private static ExecutionCandidate Candidate(
        int signalBarIndex, DirectionCandidate direction, decimal? entryPrice,
        decimal? stopLoss = null, decimal? takeProfit = null) =>
        new(signalBarIndex, Anchor.AddMinutes(signalBarIndex * 5), direction, entryPrice, null, stopLoss, takeProfit);

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // §1: no intrabar exit at all - still walks to TimeHorizon eventually (both levels configured, never
    // touched anywhere in the monitored window, including the exit bar itself).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Buy_NeitherLevelTouchedAnywhereInWindow_StillFallsThroughToTimeHorizon()
    {
        // BUY, Entry=100, StopLoss=95, TakeProfit=105. Every monitored bar (fill + exit, horizon=2) stays
        // strictly inside (95,105).
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m), // signal bar - never read for price
            Flat(5, 100m), // fill bar (index 1) - flat, no touch
            Bar(10, 100m, 102m, 98m, 101m), // index 2 - no touch
            Bar(15, 101m, 103m, 97m, 102m), // index 3 = exit bar (horizon=2) - no touch
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 95m, takeProfit: 105m), ExecutionConfiguration.Create(2));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(ExitReason.TimeHorizon, position.ExitReason);
        Assert.Equal(102m, position.ExitPrice); // exit bar's Close, unchanged fallback
        Assert.Equal(3, position.ExitBarIndex);
    }

    [Fact]
    public void Sell_NeitherLevelTouchedAnywhereInWindow_StillFallsThroughToTimeHorizon()
    {
        // SELL, Entry=100, StopLoss=105, TakeProfit=95.
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Flat(5, 100m),
            Bar(10, 100m, 102m, 98m, 99m),
            Bar(15, 99m, 103m, 97m, 98m),
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.SELL_CANDIDATE, 100m, stopLoss: 105m, takeProfit: 95m), ExecutionConfiguration.Create(2));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(ExitReason.TimeHorizon, position.ExitReason);
        Assert.Equal(98m, position.ExitPrice);
        Assert.Equal(3, position.ExitBarIndex);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // §2 (brief §16): only one level touched - priority combinations, both directions, both levels.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Buy_StopTouchedOnly_LowBelowStop_HighBelowTarget_ExitsAtStopLossLevel()
    {
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Flat(5, 100m), // fill bar, entry=100
            Bar(10, 96m, 101m, 94m, 96m), // index 2 = exit bar (horizon=1): Low=94<=95 (stop), High=101<105 (no target)
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 95m, takeProfit: 105m), ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(ExitReason.StopLoss, position.ExitReason);
        Assert.Equal(95m, position.ExitPrice); // the theoretical StopLoss level, never Low=94
        Assert.Equal(2, position.ExitBarIndex);
        Assert.Equal(1, position.HoldingBars);
    }

    [Fact]
    public void Buy_TargetTouchedOnly_HighAboveTarget_LowAboveStop_ExitsAtTakeProfitLevel()
    {
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Flat(5, 100m),
            Bar(10, 104m, 107m, 99m, 104m), // High=107>=105 (target), Low=99>95 (no stop)
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 95m, takeProfit: 105m), ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(ExitReason.TakeProfit, position.ExitReason);
        Assert.Equal(105m, position.ExitPrice); // theoretical TakeProfit level, never High=107
        Assert.Equal(2, position.ExitBarIndex);
    }

    [Fact]
    public void Sell_StopTouchedOnly_HighAboveStop_LowAboveTarget_ExitsAtStopLossLevel()
    {
        // SELL: StopLoss=105 (above entry), TakeProfit=95 (below entry).
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Flat(5, 100m),
            Bar(10, 100m, 106m, 96m, 100m), // High=106>=105 (stop), Low=96>95 (no target)
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.SELL_CANDIDATE, 100m, stopLoss: 105m, takeProfit: 95m), ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(ExitReason.StopLoss, position.ExitReason);
        Assert.Equal(105m, position.ExitPrice);
        Assert.Equal(2, position.ExitBarIndex);
    }

    [Fact]
    public void Sell_TargetTouchedOnly_LowBelowTarget_HighBelowStop_ExitsAtTakeProfitLevel()
    {
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Flat(5, 100m),
            Bar(10, 100m, 104m, 94m, 100m), // Low=94<=95 (target), High=104<105 (no stop)
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.SELL_CANDIDATE, 100m, stopLoss: 105m, takeProfit: 95m), ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(ExitReason.TakeProfit, position.ExitReason);
        Assert.Equal(95m, position.ExitPrice);
        Assert.Equal(2, position.ExitBarIndex);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // §3 (brief §7/§8/§9): same-bar ambiguity - both touched -> Ambiguous, ExitPrice = StopLoss level
    // (the documented conservative convention), symmetric across BUY/SELL.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Buy_BothLevelsTouchedSameBar_IsAmbiguous_ExitPriceIsTheStopLossLevel()
    {
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Flat(5, 100m),
            Bar(10, 100m, 106m, 94m, 100m), // High=106>=105 AND Low=94<=95: both touched
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 95m, takeProfit: 105m), ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(ExitReason.Ambiguous, position.ExitReason);
        Assert.Equal(95m, position.ExitPrice); // == candidate.StopLoss, the conservative convention
    }

    [Fact]
    public void Sell_BothLevelsTouchedSameBar_IsAmbiguous_ExitPriceIsTheStopLossLevel_MirroredSymmetry()
    {
        // SELL: StopLoss=105, TakeProfit=95. Confirm the SAME structural convention applies: ExitPrice on
        // Ambiguous is always candidate.StopLoss.Value, regardless of direction (105 here, not 95).
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Flat(5, 100m),
            Bar(10, 100m, 106m, 94m, 100m), // High=106>=105 (stop) AND Low=94<=95 (target): both touched
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.SELL_CANDIDATE, 100m, stopLoss: 105m, takeProfit: 95m), ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(ExitReason.Ambiguous, position.ExitReason);
        Assert.Equal(105m, position.ExitPrice); // == candidate.StopLoss (105), mirrors the BUY case structurally
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // §4 (brief §14-§17): multi-bar priority - whichever level is touched FIRST (walking forward) wins,
    // and the loop never reads/reacts to a later bar once it has already found an exit.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Buy_MultiBar_TargetTouchedAtTPlus2_StopTouchedAtTPlus4_TargetWinsAtTPlus2()
    {
        // entryBarIndex = 1 (t). horizon=5 so exitBarIndex=6 (t+5) - window covers t+4.
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Flat(5, 100m),                    // index 1 = t (fill bar): no touch
            Bar(10, 100m, 101m, 99m, 100m),   // index 2 = t+1: no touch
            Bar(15, 100m, 106m, 99m, 104m),   // index 3 = t+2: TakeProfit touched (High=106>=105)
            Bar(20, 100m, 101m, 99m, 100m),   // index 4 = t+3: irrelevant
            Bar(25, 100m, 101m, 90m, 95m),    // index 5 = t+4: StopLoss WOULD be touched (Low=90<=95) - must never be reached
            Bar(30, 100m, 101m, 99m, 100m),   // index 6 = t+5 = exit bar: irrelevant
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 95m, takeProfit: 105m), ExecutionConfiguration.Create(5));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(ExitReason.TakeProfit, position.ExitReason);
        Assert.Equal(105m, position.ExitPrice);
        Assert.Equal(3, position.ExitBarIndex); // exactly t+2's index - never advances to t+4
        Assert.Equal(2, position.HoldingBars);
    }

    [Fact]
    public void Buy_MultiBar_Mirror_StopTouchedAtTPlus2_TargetTouchedAtTPlus4_StopWinsAtTPlus2()
    {
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Flat(5, 100m),                    // index 1 = t
            Bar(10, 100m, 101m, 99m, 100m),   // index 2 = t+1: no touch
            Bar(15, 100m, 101m, 94m, 96m),    // index 3 = t+2: StopLoss touched (Low=94<=95)
            Bar(20, 100m, 101m, 99m, 100m),   // index 4 = t+3: irrelevant
            Bar(25, 100m, 110m, 99m, 104m),   // index 5 = t+4: TakeProfit WOULD be touched - must never be reached
            Bar(30, 100m, 101m, 99m, 100m),   // index 6 = t+5 = exit bar
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 95m, takeProfit: 105m), ExecutionConfiguration.Create(5));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(ExitReason.StopLoss, position.ExitReason);
        Assert.Equal(95m, position.ExitPrice);
        Assert.Equal(3, position.ExitBarIndex);
        Assert.Equal(2, position.HoldingBars);
    }

    [Fact]
    public void Sell_MultiBar_TargetTouchedAtTPlus2_StopTouchedAtTPlus4_TargetWinsAtTPlus2()
    {
        // SELL mirror of the first multi-bar case: StopLoss=105, TakeProfit=95.
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Flat(5, 100m),
            Bar(10, 100m, 101m, 99m, 100m),   // t+1: no touch
            Bar(15, 100m, 101m, 94m, 96m),    // t+2: TakeProfit touched (Low=94<=95)
            Bar(20, 100m, 101m, 99m, 100m),   // t+3: irrelevant
            Bar(25, 100m, 110m, 90m, 104m),   // t+4: StopLoss WOULD be touched (High=110>=105) - must never be reached
            Bar(30, 100m, 101m, 99m, 100m),   // t+5 = exit bar
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.SELL_CANDIDATE, 100m, stopLoss: 105m, takeProfit: 95m), ExecutionConfiguration.Create(5));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(ExitReason.TakeProfit, position.ExitReason);
        Assert.Equal(95m, position.ExitPrice);
        Assert.Equal(3, position.ExitBarIndex);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // §5 (brief §18): Stop/Target touched exactly ON the horizon bar itself takes priority over
    // ExitReason.TimeHorizon at that same bar.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Buy_StopTouchedExactlyOnHorizonBar_TakesPriorityOverTimeHorizon()
    {
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Flat(5, 100m),                  // index 1 = fill bar (t)
            Bar(10, 100m, 101m, 99m, 100m), // index 2 = t+1: no touch
            Bar(15, 100m, 101m, 94m, 96m),  // index 3 = t+2 = exit bar (horizon=2): StopLoss touched
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 95m, takeProfit: 105m), ExecutionConfiguration.Create(2));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.NotEqual(ExitReason.TimeHorizon, position.ExitReason);
        Assert.Equal(ExitReason.StopLoss, position.ExitReason);
        Assert.Equal(95m, position.ExitPrice);
        Assert.Equal(3, position.ExitBarIndex);
    }

    [Fact]
    public void Sell_TargetTouchedExactlyOnHorizonBar_TakesPriorityOverTimeHorizon()
    {
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Flat(5, 100m),
            Bar(10, 100m, 101m, 99m, 100m),
            Bar(15, 100m, 104m, 94m, 100m), // exit bar: Low=94<=95 (target for SELL), High=104<105 (no stop)
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.SELL_CANDIDATE, 100m, stopLoss: 105m, takeProfit: 95m), ExecutionConfiguration.Create(2));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.NotEqual(ExitReason.TimeHorizon, position.ExitReason);
        Assert.Equal(ExitReason.TakeProfit, position.ExitReason);
        Assert.Equal(95m, position.ExitPrice);
        Assert.Equal(3, position.ExitBarIndex);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // §6 (brief §9): the entry/fill bar itself CAN trigger - the window starts at entryBarIndex, not
    // entryBarIndex+1. Reasoned: EntryPrice is that bar's own Open (the bar's first chronological event),
    // so its own High/Low occur after the fill, never before it.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Buy_TakeProfitTouchedOnTheFillBarItself_ExitsImmediatelyAtEntryBarIndex()
    {
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Bar(5, 100m, 106m, 99m, 103m), // fill bar (index 1): Open=100 (entry), High=106>=105 (target touched on THIS bar)
            Bar(10, 100m, 101m, 99m, 100m),
            Bar(15, 100m, 101m, 99m, 100m),
            Bar(20, 100m, 101m, 99m, 100m),
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 95m, takeProfit: 105m), ExecutionConfiguration.Create(3));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(ExitReason.TakeProfit, position.ExitReason);
        Assert.Equal(105m, position.ExitPrice);
        Assert.Equal(1, position.ExitBarIndex); // == EntryBarIndex: exits on the very fill bar
        Assert.Equal(1, position.EntryBarIndex);
        Assert.Equal(0, position.HoldingBars);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // §7 (brief §10/§11): gap/overshoot - the triggering bar's Open already gapped past the level. Exit
    // price is STILL the theoretical StopLoss/TakeProfit level, never the gapped Open (nor the more
    // extreme High/Low) - confirms the deliberate no-slippage/no-gap-handling design choice.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Buy_OpenGapsPastStopLevel_ExitPriceIsStillTheTheoreticalStopLossLevel_NeverTheGappedOpen()
    {
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Flat(5, 100m),                    // fill bar: entry=100
            Bar(10, 90m, 91m, 85m, 88m),      // exit bar: Open=90 (already gapped below StopLoss=95), Low=85
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 95m, takeProfit: 105m), ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(ExitReason.StopLoss, position.ExitReason);
        Assert.Equal(95m, position.ExitPrice); // theoretical level
        Assert.NotEqual(90m, position.ExitPrice); // never the gapped Open
        Assert.NotEqual(85m, position.ExitPrice); // never the bar's Low either
    }

    [Fact]
    public void Sell_OpenGapsPastStopLevel_ExitPriceIsStillTheTheoreticalStopLossLevel_NeverTheGappedOpen()
    {
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Flat(5, 100m),                       // fill bar: entry=100
            Bar(10, 110m, 115m, 109m, 112m),     // exit bar: Open=110 (already gapped above StopLoss=105), High=115
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.SELL_CANDIDATE, 100m, stopLoss: 105m, takeProfit: 95m), ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(ExitReason.StopLoss, position.ExitReason);
        Assert.Equal(105m, position.ExitPrice);
        Assert.NotEqual(110m, position.ExitPrice);
        Assert.NotEqual(115m, position.ExitPrice);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // §8 (brief §19/§9, backward compatibility): Direction=NO_ACTION is unaffected by StopLoss/TakeProfit
    // being present; both-null is bit-identical to the pre-Lot-15.4 TimeHorizon-only behavior; only-one-
    // level-present never produces a spurious Ambiguous.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void NoAction_StillNotExecutable_EvenWithStopLossAndTakeProfitPresent()
    {
        var bars = new List<HistoricalBar> { Flat(0, 100m), Flat(5, 100m) };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.NO_ACTION, null, stopLoss: 95m, takeProfit: 105m), ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.NotExecutable, position.Status);
        Assert.Null(position.ExitReason);
        Assert.Null(position.ExitPrice);
    }

    [Fact]
    public void BothLevelsNull_IsBitIdenticalToPreLot154TimeHorizonOnlyBehavior()
    {
        // Exit bar has extreme High/Low that WOULD have triggered a Stop/Target if either were
        // configured - proves the intrabar block is entirely skipped when both are null.
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Flat(5, 100m),
            Bar(10, 100m, 110m, 90m, 103m),
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: null, takeProfit: null), ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(ExitReason.TimeHorizon, position.ExitReason);
        Assert.Equal(103m, position.ExitPrice); // exit bar's Close - the only pre-Lot-15.4 convention
    }

    [Fact]
    public void OnlyStopLossPresent_TakeProfitNull_NeverProducesASpuriousAmbiguous()
    {
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Flat(5, 100m),
            Bar(10, 100m, 200m, 94m, 150m), // Low touches StopLoss=95; High=200 is huge but there is NO configured TakeProfit
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 95m, takeProfit: null), ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(ExitReason.StopLoss, position.ExitReason); // never Ambiguous
        Assert.Equal(95m, position.ExitPrice);
    }

    [Fact]
    public void OnlyTakeProfitPresent_StopLossNull_NeverProducesASpuriousAmbiguous()
    {
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Flat(5, 100m),
            Bar(10, 100m, 106m, 1m, 50m), // High touches TakeProfit=105; Low=1 is huge but there is NO configured StopLoss
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: null, takeProfit: 105m), ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.Closed, position.Status);
        Assert.Equal(ExitReason.TakeProfit, position.ExitReason); // never Ambiguous, never StopLoss
        Assert.Equal(105m, position.ExitPrice);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // §9 (brief §5): invalid TradePlan direction - StopLoss/TakeProfit on the wrong side of the REAL
    // (fill-bar) EntryPrice -> PositionStatus.InvalidStopTarget, NEVER Closed.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Buy_StopLossAboveEntryPrice_IsInvalidStopTarget_NeverClosed()
    {
        var bars = new List<HistoricalBar> { Flat(0, 999m), Flat(5, 100m), Flat(10, 100m) };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 105m, takeProfit: 110m), ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.InvalidStopTarget, position.Status);
        Assert.NotEqual(PositionStatus.Closed, position.Status);
        Assert.Null(position.ExitPrice);
        Assert.Null(position.ExitReason);
        Assert.False(string.IsNullOrWhiteSpace(position.Reason));
    }

    [Fact]
    public void Buy_TakeProfitBelowEntryPrice_IsInvalidStopTarget_NeverClosed()
    {
        var bars = new List<HistoricalBar> { Flat(0, 999m), Flat(5, 100m), Flat(10, 100m) };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 90m, takeProfit: 95m), ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.InvalidStopTarget, position.Status);
        Assert.NotEqual(PositionStatus.Closed, position.Status);
        Assert.Null(position.ExitPrice);
    }

    [Fact]
    public void Sell_StopLossBelowEntryPrice_IsInvalidStopTarget_NeverClosed()
    {
        var bars = new List<HistoricalBar> { Flat(0, 999m), Flat(5, 100m), Flat(10, 100m) };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.SELL_CANDIDATE, 100m, stopLoss: 95m, takeProfit: 90m), ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.InvalidStopTarget, position.Status);
        Assert.NotEqual(PositionStatus.Closed, position.Status);
    }

    [Fact]
    public void Sell_TakeProfitAboveEntryPrice_IsInvalidStopTarget_NeverClosed()
    {
        var bars = new List<HistoricalBar> { Flat(0, 999m), Flat(5, 100m), Flat(10, 100m) };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.SELL_CANDIDATE, 100m, stopLoss: 110m, takeProfit: 105m), ExecutionConfiguration.Create(1));

        Assert.Equal(PositionStatus.InvalidStopTarget, position.Status);
        Assert.NotEqual(PositionStatus.Closed, position.Status);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // §10 (brief §21 extended): a bar in the MIDDLE of the intrabar-monitored window (not the final
    // horizon bar) failing HistoricalBar.Validate() must reject as InvalidExit immediately - never read
    // past it, never silently skipped.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void InvalidBar_InTheMiddleOfTheMonitoredWindow_IsRejectedAsInvalidExit_NeverReachesTheExitBar()
    {
        var bars = new List<HistoricalBar>
        {
            Flat(0, 999m),
            Flat(5, 100m),                   // index 1 = fill bar, valid
            Bar(10, 100m, 101m, 99m, 100m),  // index 2 = valid, no touch
            new HistoricalBar(Anchor.AddMinutes(15), Open: 92m, High: 90m, Low: 95m, Close: 92m, Volume: 10m), // index 3: High<Low, INVALID
            Bar(20, 100m, 101m, 99m, 100m),  // index 4 = exit bar (horizon=3), never reached
        };

        SimulatedPosition position = ExecutionSimulator.SimulateCore(
            bars, Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 95m, takeProfit: 105m), ExecutionConfiguration.Create(3));

        Assert.Equal(PositionStatus.InvalidExit, position.Status);
        Assert.Contains("[3]", position.Reason); // names the specific offending bar index
    }
}
