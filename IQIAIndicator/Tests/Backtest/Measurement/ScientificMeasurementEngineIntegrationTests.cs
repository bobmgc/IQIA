using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Backtest;
using IQIAIndicator.Backtest.Measurement;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Tests.BacktestTests;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Measurement;

/// <summary>
/// Sprint 15.25 (Lot 14.4, brief §17/§18/§29/§30/§39/§40). Integration-level proof that
/// <see cref="ScientificMeasurementEngine"/>, wired through <see cref="BacktestEngine.RunMeasuredSignalPipeline"/>,
/// never lets a future bar reach back and change an already-produced signal (Lot 14.3's own guarantee,
/// re-verified here at the measured-pipeline level), that the measurement itself is fully deterministic
/// and stateless, and that the signal/measurement separation is architectural, not incidental.
/// </summary>
public sealed class ScientificMeasurementEngineIntegrationTests
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

    private static MeasurementConfiguration Config() => MeasurementConfiguration.Create(10, new[] { 0.001, 0.002 });

    // ── §39/§40: minimal BacktestEngine integration, strict ordering ───────────────────────────────

    [Fact]
    public void RunMeasuredSignalPipeline_ProducesOneMeasurementPerSignalBar_InTheSameOrder()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(220, seed: 7UL);
        BacktestMeasuredSignalPipelineResult result =
            new BacktestEngine().RunMeasuredSignalPipeline(ScenarioFor(series), warmupBars: 128, Config());

        Assert.Equal(result.SignalResult.Bars.Count, result.Measurements.Count);
        for (int i = 0; i < result.Measurements.Count; i++)
            Assert.Equal(result.SignalResult.Bars[i].BarIndex, result.Measurements[i].SignalBarIndex);
    }

    // ── §17: appending future bars must never change an already-produced SIGNAL (measurement can differ) ─

    [Fact]
    public void AppendingFutureBars_NeverChangesTheSignal_ButCanResolveInsufficientFutureDataIntoAMeasurement()
    {
        HistoricalSeries full = BacktestTestSeriesBuilder.MeanRevertingOu(300, seed: 13UL);
        HistoricalSeries truncated = BacktestTestSeriesBuilder.Truncate(full, 220);

        BacktestMeasuredSignalPipelineResult shortRun =
            new BacktestEngine().RunMeasuredSignalPipeline(ScenarioFor(truncated), warmupBars: 128, Config());
        BacktestMeasuredSignalPipelineResult longRun =
            new BacktestEngine().RunMeasuredSignalPipeline(ScenarioFor(full), warmupBars: 128, Config());

        // Signal invariance over the shared prefix (brief §17 restates Lot 14.3's own guarantee).
        for (int i = 0; i < 220; i++)
        {
            BacktestSignalResult s = shortRun.SignalResult.Bars[i];
            BacktestSignalResult l = longRun.SignalResult.Bars[i];
            Assert.Equal(s.TradePlan?.Direction, l.TradePlan?.Direction);
            Assert.Equal(s.TradePlan?.EntryPrice, l.TradePlan?.EntryPrice);
            Assert.Equal(s.TradePlan?.Status, l.TradePlan?.Status);
        }

        // A signal too close to the end of the SHORT series must be InsufficientFutureData there, and
        // MAY become Measured once the longer series supplies the missing future bars - this is the
        // measurement legitimately changing, never the signal.
        bool foundResolvedCase = false;
        for (int i = 210; i < 220; i++)
        {
            if (shortRun.Measurements[i].Status == MeasurementStatus.InsufficientFutureData &&
                longRun.Measurements[i].Status == MeasurementStatus.Measured)
            {
                foundResolvedCase = true;
                break;
            }
        }
        Assert.True(foundResolvedCase,
            "Expected at least one bar near the short series' tail to move from InsufficientFutureData to Measured once more future data exists.");
    }

    // ── §18: modifying a FUTURE bar changes the measurement but never the signal ────────────────────

    [Fact]
    public void ModifyingAFutureBar_ChangesTheMeasurement_ButNeverTheAlreadyProducedSignal()
    {
        HistoricalSeries original = BacktestTestSeriesBuilder.MeanRevertingOu(200, seed: 21UL);

        var mutatedBars = new List<HistoricalBar>(original.Bars);
        // Mutate a bar strictly AFTER a mid-series signal bar, never at or before it.
        const int signalBarIndex = 150;
        HistoricalBar future = mutatedBars[signalBarIndex + 3];
        mutatedBars[signalBarIndex + 3] = future with { High = future.High + 50m, Low = future.Low + 50m, Close = future.Close + 50m };
        HistoricalSeries mutated = HistoricalSeries.Create(
            original.Symbol, original.TimeFrame, original.TimeZone, original.Provider, mutatedBars);

        BacktestMeasuredSignalPipelineResult before =
            new BacktestEngine().RunMeasuredSignalPipeline(ScenarioFor(original), warmupBars: 128, Config());
        BacktestMeasuredSignalPipelineResult after =
            new BacktestEngine().RunMeasuredSignalPipeline(ScenarioFor(mutated), warmupBars: 128, Config());

        BacktestSignalResult signalBefore = before.SignalResult.Bars[signalBarIndex];
        BacktestSignalResult signalAfter = after.SignalResult.Bars[signalBarIndex];

        Assert.Equal(signalBefore.TradePlan?.Direction, signalAfter.TradePlan?.Direction);
        Assert.Equal(signalBefore.TradePlan?.EntryPrice, signalAfter.TradePlan?.EntryPrice);
        Assert.Equal(signalBefore.TradePlan?.Status, signalAfter.TradePlan?.Status);
        Assert.Equal(signalBefore.Decision?.Winner, signalAfter.Decision?.Winner);

        MeasurementResult measurementBefore = before.Measurements[signalBarIndex];
        MeasurementResult measurementAfter = after.Measurements[signalBarIndex];

        if (measurementBefore.Status == MeasurementStatus.Measured)
        {
            // A +50 shock 3 bars into the horizon must move MFE/MAE/Return measurably - if this signal
            // happened to be BUY/SELL, the two measurements cannot be numerically identical.
            Assert.NotEqual(measurementBefore, measurementAfter);
        }
    }

    // ── §29: determinism ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SameInputs_RunTwice_ProduceTheSameDeterministicHash()
    {
        BacktestScenario scenario = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(200, seed: 4UL));

        BacktestMeasuredSignalPipelineResult first = new BacktestEngine().RunMeasuredSignalPipeline(scenario, 128, Config());
        BacktestMeasuredSignalPipelineResult second = new BacktestEngine().RunMeasuredSignalPipeline(scenario, 128, Config());

        Assert.Equal(first.DeterministicHash, second.DeterministicHash);
        Assert.Equal(first.SignalResult.DeterministicHash, second.SignalResult.DeterministicHash);
    }

    [Fact]
    public void SameInputs_RunTwice_WithARealWallClockDelay_StillProduceTheSameHash()
    {
        BacktestScenario scenario = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(150, seed: 8UL));

        BacktestMeasuredSignalPipelineResult first = new BacktestEngine().RunMeasuredSignalPipeline(scenario, 128, Config());
        System.Threading.Thread.Sleep(50);
        BacktestMeasuredSignalPipelineResult second = new BacktestEngine().RunMeasuredSignalPipeline(scenario, 128, Config());

        Assert.Equal(first.DeterministicHash, second.DeterministicHash);
    }

    [Fact]
    public void ChangingOneFutureBar_ProducesADifferentMeasurementHash()
    {
        HistoricalSeries baseline = BacktestTestSeriesBuilder.MeanRevertingOu(150, seed: 6UL);
        var mutatedBars = new List<HistoricalBar>(baseline.Bars);
        HistoricalBar original = mutatedBars[100];
        mutatedBars[100] = original with { Close = original.Close + 5m };
        HistoricalSeries mutated = HistoricalSeries.Create(
            baseline.Symbol, baseline.TimeFrame, baseline.TimeZone, baseline.Provider, mutatedBars);

        BacktestMeasuredSignalPipelineResult baselineResult =
            new BacktestEngine().RunMeasuredSignalPipeline(ScenarioFor(baseline), 128, Config());
        BacktestMeasuredSignalPipelineResult mutatedResult =
            new BacktestEngine().RunMeasuredSignalPipeline(ScenarioFor(mutated), 128, Config());

        Assert.NotEqual(baselineResult.DeterministicHash, mutatedResult.DeterministicHash);
    }

    // ── §30: run isolation (trivial by construction - ScientificMeasurementEngine is a static class
    // with no fields - but verified empirically per this lot's own established convention) ────────────

    [Fact]
    public void RunA_RunB_RunA_ProduceIdenticalResultsForTheRepeatedRunA()
    {
        BacktestScenario scenarioA = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(180, seed: 42UL));
        BacktestScenario scenarioB = ScenarioFor(BacktestTestSeriesBuilder.WhiteNoise(140, seed: 99UL));
        var engine = new BacktestEngine();

        BacktestMeasuredSignalPipelineResult runA1 = engine.RunMeasuredSignalPipeline(scenarioA, 128, Config());
        engine.RunMeasuredSignalPipeline(scenarioB, 128, Config());
        BacktestMeasuredSignalPipelineResult runA2 = engine.RunMeasuredSignalPipeline(scenarioA, 128, Config());

        Assert.Equal(runA1.DeterministicHash, runA2.DeterministicHash);
    }

    [Fact]
    public void TwoIndependentEngineInstances_OnTheSameScenario_ProduceIdenticalResults()
    {
        BacktestScenario scenario = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(160, seed: 17UL));

        BacktestMeasuredSignalPipelineResult first = new BacktestEngine().RunMeasuredSignalPipeline(scenario, 128, Config());
        BacktestMeasuredSignalPipelineResult second = new BacktestEngine().RunMeasuredSignalPipeline(scenario, 128, Config());

        Assert.Equal(first.DeterministicHash, second.DeterministicHash);
    }

    // ── No Risk Engine / execution / ATAS anywhere in this lot's wiring ─────────────────────────────

    [Fact]
    public void MeasuredPipeline_NeverProducesAMeasurementForARejectedOrExceptionBar()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(200, seed: 5UL);
        BacktestMeasuredSignalPipelineResult result =
            new BacktestEngine().RunMeasuredSignalPipeline(ScenarioFor(series), 128, Config());

        foreach (var (signal, measurement) in result.SignalResult.Bars.Zip(result.Measurements))
        {
            if (signal.Status is BacktestSignalStatus.Rejected or BacktestSignalStatus.Exception)
                Assert.Equal(MeasurementStatus.NotMeasurable, measurement.Status);
        }
    }
}
