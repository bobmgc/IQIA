using System.Threading;
using IQIAIndicator.Backtest;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.Risk;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests;

/// <summary>
/// Sprint 15.25 (Lot 14.1, brief §17 - MANDATORY). Same HistoricalSeries + Scenario + Instrument +
/// RiskPolicy + Window, run twice, must produce exactly the same BarsProcessed/BarsRejected/WarmupBars/
/// timestamps/hash. No DateTime.UtcNow may influence the result (brief §17) - proved here not by
/// inspection alone but by deliberately inserting a real wall-clock delay between the two runs: if any
/// UtcNow-derived value fed the result, the delay would make the two runs disagree.
/// </summary>
public sealed class BacktestDeterminismTests
{
    private const int WarmupBars = 30;

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, 1.5, null);

    private static BacktestScenario BuildScenario()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.WhiteNoise(160, seed: 42UL);
        var window = new BacktestWindow("DETERMINISM", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1));
        return BacktestScenario.Create(series, window, 50_000m, Spec(), Policy());
    }

    [Fact]
    public void SameScenario_RunTwice_ProducesIdenticalResult()
    {
        BacktestScenario scenario = BuildScenario();

        BacktestFoundationResult first = new BacktestEngine().Run(scenario, WarmupBars);
        BacktestFoundationResult second = new BacktestEngine().Run(scenario, WarmupBars);

        Assert.Equal(first, second);
    }

    [Fact]
    public void SameScenario_RunTwiceWithARealWallClockDelayBetween_StillProducesIdenticalHash()
    {
        // If any UtcNow-derived value influenced BarsProcessed/BarsRejected/WarmupBars/timestamps/hash,
        // this real (not simulated) delay would be enough to make the two runs disagree.
        BacktestScenario scenario = BuildScenario();

        BacktestFoundationResult first = new BacktestEngine().Run(scenario, WarmupBars);
        Thread.Sleep(50);
        BacktestFoundationResult second = new BacktestEngine().Run(scenario, WarmupBars);

        Assert.Equal(first.BarsProcessed, second.BarsProcessed);
        Assert.Equal(first.BarsRejected, second.BarsRejected);
        Assert.Equal(first.WarmupBars, second.WarmupBars);
        Assert.Equal(first.FirstTimestamp, second.FirstTimestamp);
        Assert.Equal(first.LastTimestamp, second.LastTimestamp);
        Assert.Equal(first.ScenarioId, second.ScenarioId);
        Assert.Equal(first.DeterministicHash, second.DeterministicHash);
    }

    [Fact]
    public void TwoScenariosBuiltIndependentlyFromIdenticalInputs_ProduceTheSameScenarioId()
    {
        // ScenarioId must be a pure function of the scenario's content, never a random Guid - two
        // independently-constructed BacktestScenario instances with identical inputs must agree.
        HistoricalSeries seriesA = BacktestTestSeriesBuilder.WhiteNoise(60, seed: 7UL);
        HistoricalSeries seriesB = BacktestTestSeriesBuilder.WhiteNoise(60, seed: 7UL);
        var windowA = new BacktestWindow("W", seriesA.FirstTimestamp, seriesA.LastTimestamp.AddMinutes(1));
        var windowB = new BacktestWindow("W", seriesB.FirstTimestamp, seriesB.LastTimestamp.AddMinutes(1));

        BacktestScenario scenarioA = BacktestScenario.Create(seriesA, windowA, 25_000m, Spec(), Policy());
        BacktestScenario scenarioB = BacktestScenario.Create(seriesB, windowB, 25_000m, Spec(), Policy());

        string idA = new BacktestEngine().Run(scenarioA, WarmupBars).ScenarioId;
        string idB = new BacktestEngine().Run(scenarioB, WarmupBars).ScenarioId;

        Assert.Equal(idA, idB);
    }

    [Fact]
    public void DifferentInitialCapital_ProducesADifferentScenarioId()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.WhiteNoise(60, seed: 7UL);
        var window = new BacktestWindow("W", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1));

        BacktestScenario scenarioA = BacktestScenario.Create(series, window, 25_000m, Spec(), Policy());
        BacktestScenario scenarioB = BacktestScenario.Create(series, window, 26_000m, Spec(), Policy());

        string idA = new BacktestEngine().Run(scenarioA, WarmupBars).ScenarioId;
        string idB = new BacktestEngine().Run(scenarioB, WarmupBars).ScenarioId;

        Assert.NotEqual(idA, idB);
    }

    [Fact]
    public void DifferentWarmupBars_ProducesADifferentDeterministicHash_ButSameScenarioId()
    {
        // WarmupBars is a Run() parameter, not part of the scenario's own identity - it must change the
        // per-bar fingerprint (the "warmup" flag hashed per bar) without changing ScenarioId.
        BacktestScenario scenario = BuildScenario();

        BacktestFoundationResult withWarmup30 = new BacktestEngine().Run(scenario, 30);
        BacktestFoundationResult withWarmup80 = new BacktestEngine().Run(scenario, 80);

        Assert.Equal(withWarmup30.ScenarioId, withWarmup80.ScenarioId);
        Assert.NotEqual(withWarmup30.DeterministicHash, withWarmup80.DeterministicHash);
        Assert.NotEqual(withWarmup30.WarmupBars, withWarmup80.WarmupBars);
    }
}
