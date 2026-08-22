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
/// Sprint 15.25 (Lot 14.5, brief §26/§27/§28/§29/§30/§31). Integration-level proof, through
/// <see cref="BacktestEngine.RunSimulation"/>, that entry price is never influenced by a future bar
/// (§28, MANDATORY), that appending/modifying future bars never changes a position whose exit already
/// fit inside the available data (§26/§27), and that <see cref="ExecutionSimulator"/> is deterministic
/// and stateless across runs. Covers LookAheadExecutionTests/DeterminismTests/RunIsolationTests/
/// MultipleSignalExecutionTests from brief §39.
/// </summary>
public sealed class ExecutionSimulatorIntegrationTests
{
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

    // ── §28 (MANDATORY): entry price must never be influenced by bar i+1 ───────────────────────────

    [Fact]
    public void ModifyingBarIPlusOne_NeverChangesTheEntryPriceOfAPositionCreatedAtBarI()
    {
        HistoricalSeries original = BacktestTestSeriesBuilder.MeanRevertingOu(200, seed: 21UL);

        var mutatedBars = new List<HistoricalBar>(original.Bars);
        const int signalBarIndex = 150;
        HistoricalBar nextBar = mutatedBars[signalBarIndex + 1];
        mutatedBars[signalBarIndex + 1] = nextBar with { High = nextBar.High + 200m, Low = nextBar.Low + 200m, Close = nextBar.Close + 200m };
        HistoricalSeries mutated = HistoricalSeries.Create(
            original.Symbol, original.TimeFrame, original.TimeZone, original.Provider, mutatedBars);

        BacktestSimulationResult before = new BacktestEngine().RunSimulation(ScenarioFor(original), 128, Measurement(), Exec());
        BacktestSimulationResult after = new BacktestEngine().RunSimulation(ScenarioFor(mutated), 128, Measurement(), Exec());

        SimulatedPosition positionBefore = before.ExecutionResult.Positions[signalBarIndex];
        SimulatedPosition positionAfter = after.ExecutionResult.Positions[signalBarIndex];

        Assert.Equal(positionBefore.EntryPrice, positionAfter.EntryPrice);
        Assert.Equal(positionBefore.EntryTimestamp, positionAfter.EntryTimestamp);
        Assert.Equal(positionBefore.Direction, positionAfter.Direction);
        Assert.Equal(positionBefore.Status, positionAfter.Status);
    }

    // ── §26/§27: horizon boundary and look-ahead at the integration level ──────────────────────────

    [Fact]
    public void AppendingFutureBars_NeverChangesAPositionWhoseExitAlreadyFitInsideTheOriginalSeries()
    {
        HistoricalSeries full = BacktestTestSeriesBuilder.MeanRevertingOu(320, seed: 13UL);
        HistoricalSeries truncated = BacktestTestSeriesBuilder.Truncate(full, 220);

        BacktestSimulationResult shortRun = new BacktestEngine().RunSimulation(ScenarioFor(truncated), 128, Measurement(), Exec());
        BacktestSimulationResult longRun = new BacktestEngine().RunSimulation(ScenarioFor(full), 128, Measurement(), Exec());

        // Every signal whose exit (i+10) already fit inside the 220-bar series must produce the EXACT
        // same position whether or not more future data exists beyond it.
        for (int i = 0; i < 210; i++)
        {
            SimulatedPosition s = shortRun.ExecutionResult.Positions[i];
            SimulatedPosition l = longRun.ExecutionResult.Positions[i];
            Assert.Equal(s, l);
        }
    }

    [Fact]
    public void ModifyingBarEleven_NeverChangesAPositionWithExitAtBarTen()
    {
        HistoricalSeries original = BacktestTestSeriesBuilder.MeanRevertingOu(150, seed: 9UL);
        var mutatedBars = new List<HistoricalBar>(original.Bars);
        HistoricalBar bar11 = mutatedBars[11];
        mutatedBars[11] = bar11 with { High = 100000m, Low = 1m, Close = 500m };
        HistoricalSeries mutated = HistoricalSeries.Create(
            original.Symbol, original.TimeFrame, original.TimeZone, original.Provider, mutatedBars);

        var config = Exec(10);
        SimulatedPosition before = ExecutionSimulator.SimulateFromSignal(
            original, new BacktestEngine().RunSignalPipeline(ScenarioFor(original), 5).Bars[0], config);
        SimulatedPosition after = ExecutionSimulator.SimulateFromSignal(
            mutated, new BacktestEngine().RunSignalPipeline(ScenarioFor(mutated), 5).Bars[0], config);

        Assert.Equal(before, after);
    }

    // ── §29: determinism ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SameInputs_RunTwice_ProduceTheSameDeterministicHash()
    {
        BacktestScenario scenario = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(200, seed: 4UL));

        BacktestSimulationResult first = new BacktestEngine().RunSimulation(scenario, 128, Measurement(), Exec());
        BacktestSimulationResult second = new BacktestEngine().RunSimulation(scenario, 128, Measurement(), Exec());

        Assert.Equal(first.ExecutionResult.DeterministicHash, second.ExecutionResult.DeterministicHash);
        Assert.Equal(first.SignalResult.DeterministicHash, second.SignalResult.DeterministicHash);
    }

    [Fact]
    public void SameInputs_RunTwice_WithARealWallClockDelay_StillProduceTheSameHash()
    {
        BacktestScenario scenario = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(150, seed: 8UL));

        BacktestSimulationResult first = new BacktestEngine().RunSimulation(scenario, 128, Measurement(), Exec());
        System.Threading.Thread.Sleep(50);
        BacktestSimulationResult second = new BacktestEngine().RunSimulation(scenario, 128, Measurement(), Exec());

        Assert.Equal(first.ExecutionResult.DeterministicHash, second.ExecutionResult.DeterministicHash);
    }

    [Fact]
    public void ChangingOneFutureBar_ProducesADifferentExecutionHash()
    {
        HistoricalSeries baseline = BacktestTestSeriesBuilder.MeanRevertingOu(150, seed: 6UL);
        var mutatedBars = new List<HistoricalBar>(baseline.Bars);
        HistoricalBar original = mutatedBars[100];
        mutatedBars[100] = original with { Close = original.Close + 5m };
        HistoricalSeries mutated = HistoricalSeries.Create(
            baseline.Symbol, baseline.TimeFrame, baseline.TimeZone, baseline.Provider, mutatedBars);

        BacktestSimulationResult baselineResult = new BacktestEngine().RunSimulation(ScenarioFor(baseline), 128, Measurement(), Exec());
        BacktestSimulationResult mutatedResult = new BacktestEngine().RunSimulation(ScenarioFor(mutated), 128, Measurement(), Exec());

        Assert.NotEqual(baselineResult.ExecutionResult.DeterministicHash, mutatedResult.ExecutionResult.DeterministicHash);
    }

    // ── §30: run isolation ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RunA_RunB_RunA_ProduceIdenticalResultsForTheRepeatedRunA()
    {
        BacktestScenario scenarioA = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(180, seed: 42UL));
        BacktestScenario scenarioB = ScenarioFor(BacktestTestSeriesBuilder.WhiteNoise(140, seed: 99UL));
        var engine = new BacktestEngine();

        BacktestSimulationResult runA1 = engine.RunSimulation(scenarioA, 128, Measurement(), Exec());
        engine.RunSimulation(scenarioB, 128, Measurement(), Exec());
        BacktestSimulationResult runA2 = engine.RunSimulation(scenarioA, 128, Measurement(), Exec());

        Assert.Equal(runA1.ExecutionResult.DeterministicHash, runA2.ExecutionResult.DeterministicHash);
    }

    [Fact]
    public void TwoIndependentEngineInstances_OnTheSameScenario_ProduceIdenticalResults()
    {
        BacktestScenario scenario = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(160, seed: 17UL));

        BacktestSimulationResult first = new BacktestEngine().RunSimulation(scenario, 128, Measurement(), Exec());
        BacktestSimulationResult second = new BacktestEngine().RunSimulation(scenario, 128, Measurement(), Exec());

        Assert.Equal(first.ExecutionResult.DeterministicHash, second.ExecutionResult.DeterministicHash);
    }

    // ── §31/§32: multiple signals, no portfolio accounting, overlapping positions allowed ──────────

    [Fact]
    public void MultipleSignals_EachProducesAnIndependentPosition_OverlapsAreNeverSuppressed()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(300, seed: 23UL, kappa: 0.8m);
        BacktestSimulationResult result = new BacktestEngine().RunSimulation(ScenarioFor(series), 128, Measurement(), Exec());

        // brief §31: one independent position per signal, regardless of any other signal - proven
        // structurally (every bucket sums back to TotalCount, so no position is ever merged or dropped).
        Assert.Equal(result.SignalResult.Bars.Count, result.ExecutionResult.Positions.Count);
        Assert.Equal(result.ExecutionResult.TotalCount,
            result.ExecutionResult.ClosedCount + result.ExecutionResult.NotExecutableCount +
            result.ExecutionResult.InvalidEntryCount + result.ExecutionResult.InsufficientFutureDataCount +
            result.ExecutionResult.InvalidExitCount);

        // brief §32: overlapping positions (two Closed positions whose EntryBarIndex are within
        // HorizonBars of each other) must both exist, unconstrained - find at least one such pair in this
        // dataset and confirm neither was suppressed or merged into the other.
        var closedIndices = new List<int>();
        foreach (SimulatedPosition position in result.ExecutionResult.Positions)
            if (position.Status == PositionStatus.Closed)
                closedIndices.Add(position.EntryBarIndex);

        bool foundOverlap = false;
        for (int i = 1; i < closedIndices.Count; i++)
        {
            if (closedIndices[i] - closedIndices[i - 1] < 10)
            {
                foundOverlap = true;
                break;
            }
        }
        Assert.True(foundOverlap, "Expected at least one pair of overlapping Closed positions in this dataset to demonstrate brief §32 (no positioning policy suppresses overlaps in this lot).");
    }
}
