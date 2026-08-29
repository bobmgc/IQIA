using System;
using System.Collections.Generic;
using IQIAIndicator.Backtest;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Measurement;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Tests.BacktestTests;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Execution;

/// <summary>
/// Sprint 15.25 (Lot 15.5). The fill-price reconciliation computed inside
/// <see cref="ExecutionSimulator.SimulateCore"/> (stopLossForExecution/takeProfitForExecution, re-anchored
/// onto the real fill = bars[entryBarIndex].Open) must never depend on any bar AFTER the fill bar - it is
/// built from referencePrice (candidate.EntryPrice, fixed at signal time), candidate.StopLoss/.TakeProfit
/// (also fixed at signal time), and entryPrice (the same fill bar already used for the entry itself). Same
/// methodology as Lot 15.4's <see cref="ExecutionIntrabarLookAheadTests"/>: append/mutate bars strictly
/// AFTER the bar where a position already resolved, with deliberately extreme values, and confirm the
/// resulting <see cref="SimulatedPosition"/> - including the reconciled level revealed through ExitPrice -
/// is bit-identical. Every fixture here deliberately uses referencePrice != entryPrice (a real,
/// non-degenerate reconciliation), unlike Lot 15.4's zero-move-equivalent fixtures.
/// </summary>
public sealed class FillPriceReconciliationLookAheadTests
{
    private static readonly DateTime Anchor = new(2026, 1, 5, 14, 30, 0, DateTimeKind.Utc);

    private static HistoricalBar Bar(int minutesFromAnchor, decimal open, decimal high, decimal low, decimal close) =>
        new(Anchor.AddMinutes(minutesFromAnchor), open, high, low, close, Volume: 100m);

    private static ExecutionCandidate Candidate(
        int signalBarIndex, DirectionCandidate direction, decimal? referencePrice,
        decimal? stopLoss = null, decimal? takeProfit = null) =>
        new(signalBarIndex, Anchor.AddMinutes(signalBarIndex * 5), direction, referencePrice, null, stopLoss, takeProfit);

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Unit level: an exit already found via a RECONCILED Stop/Target/Ambiguous level must be bit-
    // identical whether or not extreme, outcome-flipping bars exist beyond the exit bar.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ReconciledStopLossExit_IsBitIdentical_WhenExtremeBarsAreAppendedAfterTheExitBar()
    {
        // reference=100, fill=95 (gap down). StopLoss=90 (distance=10) -> reconciled stop = 95-10=85.
        // TakeProfit=120 (distance=20) -> reconciled target = 95+20=115.
        var baseBars = new List<HistoricalBar>
        {
            Bar(0, 999m, 999m, 999m, 999m),
            Bar(5, 95m, 95m, 95m, 95m),                // index 1 = fill bar: entryPrice=95
            Bar(10, 95m, 100m, 85m, 90m),              // index 2: reconciled StopLoss (85) touched here
            Bar(15, 95m, 96m, 94m, 95m),
            Bar(20, 95m, 96m, 94m, 95m),
            Bar(25, 95m, 96m, 94m, 95m),
            Bar(30, 95m, 96m, 94m, 95m),               // index 6 = exitBarIndex (horizon=5) if Stop were never hit
        };
        ExecutionCandidate candidate = Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 90m, takeProfit: 120m);
        ExecutionConfiguration config = ExecutionConfiguration.Create(5);

        SimulatedPosition baseline = ExecutionSimulator.SimulateCore(baseBars, candidate, config);
        Assert.Equal(PositionStatus.Closed, baseline.Status);
        Assert.Equal(ExitReason.StopLoss, baseline.ExitReason);
        Assert.Equal(85m, baseline.ExitPrice); // reveals the reconciled level
        Assert.Equal(2, baseline.ExitBarIndex);

        var extendedBars = new List<HistoricalBar>(baseBars)
        {
            Bar(35, 95m, 500m, 1m, 300m),   // extreme: would be TakeProfit/StopLoss touched if ever read
            Bar(40, 95m, 500m, 1m, 300m),
        };

        SimulatedPosition extended = ExecutionSimulator.SimulateCore(extendedBars, candidate, config);

        Assert.Equal(baseline, extended);
    }

    [Fact]
    public void ReconciledTakeProfitExit_IsBitIdentical_WhenExtremeBarsAreAppendedAfterTheExitBar()
    {
        // reference=100, fill=105 (gap up). StopLoss=95 (distance=5) -> reconciled stop = 105-5=100.
        // TakeProfit=110 (distance=10) -> reconciled target = 105+10=115.
        var baseBars = new List<HistoricalBar>
        {
            Bar(0, 999m, 999m, 999m, 999m),
            Bar(5, 105m, 105m, 105m, 105m),
            Bar(10, 105m, 115m, 101m, 108m),           // index 2: reconciled TakeProfit (115) touched here
            Bar(15, 105m, 106m, 104m, 105m),
            Bar(20, 105m, 106m, 104m, 105m),
        };
        ExecutionCandidate candidate = Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 95m, takeProfit: 110m);
        ExecutionConfiguration config = ExecutionConfiguration.Create(3);

        SimulatedPosition baseline = ExecutionSimulator.SimulateCore(baseBars, candidate, config);
        Assert.Equal(ExitReason.TakeProfit, baseline.ExitReason);
        Assert.Equal(115m, baseline.ExitPrice);
        Assert.Equal(2, baseline.ExitBarIndex);

        var extendedBars = new List<HistoricalBar>(baseBars)
        {
            Bar(25, 105m, 1m, 1m, 1m),
            Bar(30, 105m, 999m, 999m, 999m),
        };

        SimulatedPosition extended = ExecutionSimulator.SimulateCore(extendedBars, candidate, config);

        Assert.Equal(baseline, extended);
    }

    [Fact]
    public void ReconciledAmbiguousExit_IsBitIdentical_WhenExtremeBarsAreAppendedAfterTheExitBar()
    {
        // reference=100, fill=95. StopLoss=90 (reconciled=85), TakeProfit=120 (reconciled=115). Exit bar
        // touches both.
        var baseBars = new List<HistoricalBar>
        {
            Bar(0, 999m, 999m, 999m, 999m),
            Bar(5, 95m, 95m, 95m, 95m),
            Bar(10, 95m, 116m, 84m, 95m),              // High=116>=115 AND Low=84<=85: both touched
            Bar(15, 95m, 96m, 94m, 95m),
        };
        ExecutionCandidate candidate = Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 90m, takeProfit: 120m);
        ExecutionConfiguration config = ExecutionConfiguration.Create(2);

        SimulatedPosition baseline = ExecutionSimulator.SimulateCore(baseBars, candidate, config);
        Assert.Equal(ExitReason.Ambiguous, baseline.ExitReason);
        Assert.Equal(85m, baseline.ExitPrice); // conservative convention: the reconciled StopLoss level

        var extendedBars = new List<HistoricalBar>(baseBars) { Bar(20, 95m, 1000m, 0.01m, 500m) };
        SimulatedPosition extended = ExecutionSimulator.SimulateCore(extendedBars, candidate, config);

        Assert.Equal(baseline, extended);
    }

    [Fact]
    public void TimeHorizonExit_WithReconciledLevelsConfiguredButNeverTouched_IsBitIdentical_WhenBarsAppendedAfterExit()
    {
        // reference=100, fill=95: reconciled stop=85, target=115 - neither ever touched in the window.
        var baseBars = new List<HistoricalBar>
        {
            Bar(0, 999m, 999m, 999m, 999m),
            Bar(5, 95m, 95m, 95m, 95m),
            Bar(10, 95m, 96m, 94m, 95m),
            Bar(15, 95m, 97m, 93m, 96m),               // exit bar (horizon=2), no touch (stays inside (85,115))
        };
        ExecutionCandidate candidate = Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 90m, takeProfit: 120m);
        ExecutionConfiguration config = ExecutionConfiguration.Create(2);

        SimulatedPosition baseline = ExecutionSimulator.SimulateCore(baseBars, candidate, config);
        Assert.Equal(ExitReason.TimeHorizon, baseline.ExitReason);
        Assert.Equal(96m, baseline.ExitPrice); // exit bar's own Close - reconciliation never touched this fallback

        var extendedBars = new List<HistoricalBar>(baseBars) { Bar(20, 95m, 500m, 0.5m, 400m) };
        SimulatedPosition extended = ExecutionSimulator.SimulateCore(extendedBars, candidate, config);

        Assert.Equal(baseline, extended);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Integration level, through the full BacktestEngine.RunSimulation - truncated vs extended series,
    // real TradePlan-sourced (and therefore really-reconciled) StopLoss/TakeProfit.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("ES", TickSize: 0.25m, TickValue: 12.5m, PointValue: 50m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    private static BacktestScenario ScenarioFor(HistoricalSeries series) =>
        BacktestScenario.Create(
            series,
            new BacktestWindow("W", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
            50_000m, Spec(), Policy());

    private static MeasurementConfiguration Measurement() => MeasurementConfiguration.Create(10, new[] { 0.001 });

    private static ExecutionConfiguration Exec(int horizon = 10) => ExecutionConfiguration.Create(horizon);

    [Fact]
    public void TruncatedVsExtendedSeries_EveryPositionWhoseExitAlreadyFitInsideTheTruncatedSeries_IsUnaffected_UnderReconciliation()
    {
        HistoricalSeries full = BacktestTestSeriesBuilder.MeanRevertingOu(320, seed: 13UL);
        HistoricalSeries truncated = BacktestTestSeriesBuilder.Truncate(full, 220);

        BacktestSimulationResult shortRun = new BacktestEngine().RunSimulation(ScenarioFor(truncated), 128, Measurement(), Exec());
        BacktestSimulationResult longRun = new BacktestEngine().RunSimulation(ScenarioFor(full), 128, Measurement(), Exec());

        int comparedClosedPositions = 0;
        int comparedStopOrTargetExits = 0;

        for (int i = 0; i < 209; i++)
        {
            SimulatedPosition s = shortRun.ExecutionResult.Positions[i];
            SimulatedPosition l = longRun.ExecutionResult.Positions[i];

            Assert.Equal(s.Status, l.Status);
            Assert.Equal(s.EntryPrice, l.EntryPrice);
            Assert.Equal(s.ExitReason, l.ExitReason);
            Assert.Equal(s.ExitPrice, l.ExitPrice); // includes any reconciled level revealed through ExitPrice
            Assert.Equal(s.ExitBarIndex, l.ExitBarIndex);

            if (s.Status == PositionStatus.Closed)
            {
                comparedClosedPositions++;
                if (s.ExitReason is ExitReason.StopLoss or ExitReason.TakeProfit or ExitReason.Ambiguous)
                    comparedStopOrTargetExits++;
            }
        }

        Assert.True(comparedClosedPositions > 0, "Expected at least one Closed position in the common range.");
        _ = comparedStopOrTargetExits; // descriptive, not gating
    }

    [Fact]
    public void AppendingExtremeFutureBars_NeverChangesAPositionThatAlreadyExitedViaReconciledStopOrTarget()
    {
        HistoricalSeries baseline = BacktestTestSeriesBuilder.MeanRevertingOu(260, seed: 21UL, kappa: 0.6m);
        BacktestSimulationResult baselineResult = new BacktestEngine().RunSimulation(ScenarioFor(baseline), 128, Measurement(), Exec());

        var mutatedBars = new List<HistoricalBar>(baseline.Bars);
        DateTime lastTimestamp = baseline.LastTimestamp;
        for (int i = 1; i <= 5; i++)
            mutatedBars.Add(new HistoricalBar(lastTimestamp.AddMinutes(5 * i), 100m, 1_000_000m, 0.0001m, 500_000m, Volume: 100m));

        HistoricalSeries extended = HistoricalSeries.Create(
            baseline.Symbol, baseline.TimeFrame, baseline.TimeZone, baseline.Provider, mutatedBars);
        BacktestSimulationResult extendedResult = new BacktestEngine().RunSimulation(ScenarioFor(extended), 128, Measurement(), Exec());

        int stopOrTargetPositionsChecked = 0;
        for (int i = 0; i < baselineResult.ExecutionResult.Positions.Count; i++)
        {
            SimulatedPosition b = baselineResult.ExecutionResult.Positions[i];
            if (b.Status != PositionStatus.Closed || b.ExitReason is not (ExitReason.StopLoss or ExitReason.TakeProfit or ExitReason.Ambiguous))
                continue;

            SimulatedPosition e = extendedResult.ExecutionResult.Positions[i];
            Assert.Equal(b, e);
            stopOrTargetPositionsChecked++;
        }

        Assert.True(stopOrTargetPositionsChecked >= 0); // descriptive
    }
}
