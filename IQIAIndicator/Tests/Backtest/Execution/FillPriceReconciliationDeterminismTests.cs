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
/// Sprint 15.25 (Lot 15.5). Determinism and run isolation for the fill-price reconciliation specifically
/// (referencePrice != entryPrice, so a real, non-degenerate reconciliation is exercised on every fixture
/// here) - same methodology as Lot 15.4's <see cref="ExecutionIntrabarDeterminismTests"/>.
/// <see cref="ExecutionSimulator"/> remains a static class with no fields; these tests confirm that holds
/// empirically for the reconciliation derivation this lot added.
/// </summary>
public sealed class FillPriceReconciliationDeterminismTests
{
    private static readonly DateTime Anchor = new(2026, 1, 5, 14, 30, 0, DateTimeKind.Utc);

    private static HistoricalBar Bar(int minutesFromAnchor, decimal open, decimal high, decimal low, decimal close) =>
        new(Anchor.AddMinutes(minutesFromAnchor), open, high, low, close, Volume: 100m);

    private static ExecutionCandidate Candidate(
        int signalBarIndex, DirectionCandidate direction, decimal? referencePrice,
        decimal? stopLoss = null, decimal? takeProfit = null) =>
        new(signalBarIndex, Anchor.AddMinutes(signalBarIndex * 5), direction, referencePrice, null, stopLoss, takeProfit);

    // reference=100, fill=95: reconciled stop=85, reconciled target=115.
    private static List<HistoricalBar> ReconciledStopLossFixture() => new()
    {
        Bar(0, 999m, 999m, 999m, 999m),
        Bar(5, 95m, 95m, 95m, 95m),
        Bar(10, 95m, 100m, 85m, 90m),
        Bar(15, 95m, 96m, 94m, 95m),
    };

    // reference=100, fill=105: reconciled stop=100, reconciled target=115.
    private static List<HistoricalBar> ReconciledTakeProfitFixture() => new()
    {
        Bar(0, 999m, 999m, 999m, 999m),
        Bar(5, 105m, 105m, 105m, 105m),
        Bar(10, 105m, 115m, 101m, 108m),
        Bar(15, 105m, 106m, 104m, 105m),
    };

    // reference=100, fill=95: reconciled stop=85, reconciled target=115; exit bar touches both.
    private static List<HistoricalBar> ReconciledAmbiguousFixture() => new()
    {
        Bar(0, 999m, 999m, 999m, 999m),
        Bar(5, 95m, 95m, 95m, 95m),
        Bar(10, 95m, 116m, 84m, 95m),
        Bar(15, 95m, 96m, 94m, 95m),
    };

    private static ExecutionCandidate StopLossCandidate() =>
        Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 90m, takeProfit: 120m);

    private static ExecutionCandidate TakeProfitCandidate() =>
        Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 95m, takeProfit: 110m);

    private static ExecutionCandidate AmbiguousCandidate() =>
        Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 90m, takeProfit: 120m);

    private static ExecutionConfiguration StandardConfig() => ExecutionConfiguration.Create(2);

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Same TradePlan + bars + config -> bit-identical reconciled SimulatedPosition.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("StopLoss")]
    [InlineData("TakeProfit")]
    [InlineData("Ambiguous")]
    public void SameInputs_RunTwice_ProduceBitIdenticalReconciledSimulatedPositions(string fixtureName)
    {
        (List<HistoricalBar> bars, ExecutionCandidate candidate, ExitReason expected) = fixtureName switch
        {
            "StopLoss" => (ReconciledStopLossFixture(), StopLossCandidate(), ExitReason.StopLoss),
            "TakeProfit" => (ReconciledTakeProfitFixture(), TakeProfitCandidate(), ExitReason.TakeProfit),
            "Ambiguous" => (ReconciledAmbiguousFixture(), AmbiguousCandidate(), ExitReason.Ambiguous),
            _ => throw new ArgumentOutOfRangeException(nameof(fixtureName))
        };
        ExecutionConfiguration config = StandardConfig();

        SimulatedPosition instanceA = ExecutionSimulator.SimulateCore(bars, candidate, config);
        SimulatedPosition instanceB = ExecutionSimulator.SimulateCore(bars, candidate, config);

        Assert.Equal(instanceA, instanceB); // record equality: every field, including the reconciled ExitPrice
        Assert.Equal(expected, instanceA.ExitReason);
        Assert.Equal(instanceA.ExitPrice, instanceB.ExitPrice);
        Assert.Equal(instanceA.ExitBarIndex, instanceB.ExitBarIndex);
    }

    [Fact]
    public void SameInputs_RunTwice_WithARealWallClockDelayBetweenRuns_StillProducesTheSameReconciledResult()
    {
        List<HistoricalBar> bars = ReconciledStopLossFixture();
        ExecutionCandidate candidate = StopLossCandidate();
        ExecutionConfiguration config = StandardConfig();

        SimulatedPosition first = ExecutionSimulator.SimulateCore(bars, candidate, config);
        System.Threading.Thread.Sleep(50);
        SimulatedPosition second = ExecutionSimulator.SimulateCore(bars, candidate, config);

        Assert.Equal(first, second);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Run isolation: RunA -> RunB (different scenario) -> RunA, repeated RunA must be identical.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RunA_RunB_RunA_Interleaved_ProducesIdenticalReconciledResultsForTheRepeatedRunA()
    {
        List<HistoricalBar> barsA = ReconciledStopLossFixture();
        ExecutionCandidate candidateA = StopLossCandidate();
        ExecutionConfiguration configA = StandardConfig();

        List<HistoricalBar> barsB = ReconciledTakeProfitFixture();
        ExecutionCandidate candidateB = Candidate(0, DirectionCandidate.SELL_CANDIDATE, 100m, stopLoss: 105m, takeProfit: 90m);
        ExecutionConfiguration configB = ExecutionConfiguration.Create(7);

        SimulatedPosition runA1 = ExecutionSimulator.SimulateCore(barsA, candidateA, configA);
        SimulatedPosition runBIntervening = ExecutionSimulator.SimulateCore(barsB, candidateB, configB);
        SimulatedPosition runA2 = ExecutionSimulator.SimulateCore(barsA, candidateA, configA);

        Assert.Equal(runA1, runA2);
        Assert.Equal(ExitReason.StopLoss, runA1.ExitReason);
        Assert.Equal(85m, runA1.ExitPrice); // the reconciled level, stable across the interleaved run
        _ = runBIntervening;
    }

    [Fact]
    public void ManyInterleavedRunsOfDifferentReconciledExitReasons_NeverCrossContaminate()
    {
        for (int iteration = 0; iteration < 25; iteration++)
        {
            SimulatedPosition stop = ExecutionSimulator.SimulateCore(ReconciledStopLossFixture(), StopLossCandidate(), StandardConfig());
            SimulatedPosition target = ExecutionSimulator.SimulateCore(ReconciledTakeProfitFixture(), TakeProfitCandidate(), StandardConfig());
            SimulatedPosition ambiguous = ExecutionSimulator.SimulateCore(ReconciledAmbiguousFixture(), AmbiguousCandidate(), StandardConfig());

            Assert.Equal(ExitReason.StopLoss, stop.ExitReason);
            Assert.Equal(85m, stop.ExitPrice);
            Assert.Equal(ExitReason.TakeProfit, target.ExitReason);
            Assert.Equal(115m, target.ExitPrice);
            Assert.Equal(ExitReason.Ambiguous, ambiguous.ExitReason);
            Assert.Equal(85m, ambiguous.ExitPrice);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Determinism/run-isolation through ExecutionSimulator.SimulateAll and BacktestEngine.RunSimulation -
    // RunA -> RunB -> RunA interleaved on real (TradePlan-sourced, really-reconciled) synthetic data.
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
    public void SimulateAll_SameSeriesAndSignals_RunTwice_ProducesBitIdenticalExecutionResult()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(240, seed: 33UL);
        BacktestScenario scenario = ScenarioFor(series);

        BacktestSimulationResult resultA = new BacktestEngine().RunSimulation(scenario, 128, Measurement(), Exec());
        BacktestSimulationResult resultB = new BacktestEngine().RunSimulation(scenario, 128, Measurement(), Exec());

        Assert.Equal(resultA.ExecutionResult.DeterministicHash, resultB.ExecutionResult.DeterministicHash);
        Assert.Equal(resultA.ExecutionResult.InvalidStopTargetCount, resultB.ExecutionResult.InvalidStopTargetCount);
        Assert.Equal(resultA.ExecutionResult.ClosedCount, resultB.ExecutionResult.ClosedCount);
        for (int i = 0; i < resultA.ExecutionResult.Positions.Count; i++)
            Assert.Equal(resultA.ExecutionResult.Positions[i], resultB.ExecutionResult.Positions[i]);
    }

    [Fact]
    public void RunA_RunB_RunA_Interleaved_ThroughBacktestEngine_ProducesIdenticalResultsForTheRepeatedRunA()
    {
        HistoricalSeries seriesA = BacktestTestSeriesBuilder.MeanRevertingOu(240, seed: 33UL);
        HistoricalSeries seriesB = BacktestTestSeriesBuilder.MeanRevertingOu(180, seed: 71UL, kappa: 0.8m);
        BacktestScenario scenarioA = ScenarioFor(seriesA);
        BacktestScenario scenarioB = ScenarioFor(seriesB);

        BacktestSimulationResult runA1 = new BacktestEngine().RunSimulation(scenarioA, 128, Measurement(), Exec());
        BacktestSimulationResult runBIntervening = new BacktestEngine().RunSimulation(scenarioB, 100, Measurement(), Exec());
        BacktestSimulationResult runA2 = new BacktestEngine().RunSimulation(scenarioA, 128, Measurement(), Exec());

        Assert.Equal(runA1.ExecutionResult.DeterministicHash, runA2.ExecutionResult.DeterministicHash);
        Assert.Equal(runA1.ExecutionResult.InvalidStopTargetCount, runA2.ExecutionResult.InvalidStopTargetCount);
        for (int i = 0; i < runA1.ExecutionResult.Positions.Count; i++)
            Assert.Equal(runA1.ExecutionResult.Positions[i], runA2.ExecutionResult.Positions[i]);

        _ = runBIntervening;
    }
}
