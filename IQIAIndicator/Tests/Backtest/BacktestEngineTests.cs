using System;
using System.Linq;
using IQIAIndicator.Backtest;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.Risk;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests;

/// <summary>Sprint 15.25 (Lot 14.1). Engine-level behaviour: bar counting, warmup accounting, and the
/// architectural fact that a scenario built through the public API (HistoricalSeries + a valid
/// InstrumentRiskSpecification) can never actually produce a MarketContextValidator rejection - see
/// BarsRejected_IsAlwaysZero_ForAScenarioBuiltThroughTheValidatingConstructors below.</summary>
public sealed class BacktestEngineTests
{
    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("ES", TickSize: 0.25m, TickValue: 12.5m, PointValue: 50m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    private static BacktestScenario ScenarioOverBars(int count)
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.WhiteNoise(count, seed: 42UL);
        var window = new BacktestWindow("W", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1));
        return BacktestScenario.Create(series, window, 50_000m, Spec(), Policy());
    }

    [Fact]
    public void Run_ProcessesEveryBar_InChronologicalOrder()
    {
        BacktestScenario scenario = ScenarioOverBars(50);
        var observer = new CapturingBarObserver();

        BacktestFoundationResult result = new BacktestEngine().Run(scenario, warmupBars: 10, observer);

        Assert.Equal(50, result.BarsProcessed);
        Assert.Equal(0, result.BarsRejected);
        Assert.Equal(50, observer.ProcessedBars.Count);
        for (int i = 0; i < observer.ProcessedBars.Count; i++)
            Assert.Equal(i, observer.ProcessedBars[i].Index);
    }

    [Fact]
    public void WarmupBars_CountsOnlyProcessedBarsBelowTheConfiguredThreshold()
    {
        BacktestScenario scenario = ScenarioOverBars(20);

        BacktestFoundationResult result = new BacktestEngine().Run(scenario, warmupBars: 7);

        Assert.Equal(7, result.WarmupBars);
        Assert.Equal(20, result.BarsProcessed);
    }

    [Fact]
    public void WarmupBars_GreaterThanSeriesLength_CountsEveryProcessedBar_NeverThrows()
    {
        BacktestScenario scenario = ScenarioOverBars(10);

        BacktestFoundationResult result = new BacktestEngine().Run(scenario, warmupBars: 1000);

        Assert.Equal(10, result.WarmupBars);
        Assert.Equal(10, result.BarsProcessed);
    }

    [Fact]
    public void WarmupBars_Zero_MeansNoBarIsCountedAsWarmup()
    {
        BacktestScenario scenario = ScenarioOverBars(10);

        BacktestFoundationResult result = new BacktestEngine().Run(scenario, warmupBars: 0);

        Assert.Equal(0, result.WarmupBars);
    }

    [Fact]
    public void NegativeWarmupBars_Throws()
    {
        BacktestScenario scenario = ScenarioOverBars(10);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BacktestEngine().Run(scenario, warmupBars: -1));
    }

    [Fact]
    public void NullScenario_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new BacktestEngine().Run(null!, warmupBars: 10));
    }

    [Fact]
    public void FirstAndLastTimestamp_ReflectTheSeriesCoverage_NotJustProcessedBars()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.WhiteNoise(30, seed: 42UL);
        var window = new BacktestWindow("W", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1));
        BacktestScenario scenario = BacktestScenario.Create(series, window, 50_000m, Spec(), Policy());

        BacktestFoundationResult result = new BacktestEngine().Run(scenario, warmupBars: 5);

        Assert.Equal(series.FirstTimestamp, result.FirstTimestamp);
        Assert.Equal(series.LastTimestamp, result.LastTimestamp);
    }

    /// <summary>
    /// Architectural fact, not an incidental test outcome: HistoricalSeries.Create already enforces
    /// every per-bar check MarketContextValidator would apply (and more - see HistoricalBar's doc
    /// comment), and BacktestScenario.Create already enforces InstrumentRiskSpecification.IsValid (which
    /// implies TickSize &gt; 0, the one MarketContextValidator check that is NOT a per-bar property).
    /// Consequently, a scenario built through these public, validating constructors can never make
    /// MarketContextFactory produce a context MarketContextValidator rejects - BarsRejected is always 0.
    /// The rejection path in BacktestEngine.Run still exists deliberately (defence in depth, and the
    /// contract a future lot's alternative context-construction path must still honour), but this lot's
    /// actual, reachable code paths never exercise it - documented here rather than left implicit.
    /// </summary>
    [Fact]
    public void BarsRejected_IsAlwaysZero_ForAScenarioBuiltThroughTheValidatingConstructors()
    {
        BacktestScenario scenario = ScenarioOverBars(75);
        var observer = new CapturingBarObserver();

        BacktestFoundationResult result = new BacktestEngine().Run(scenario, warmupBars: 30, observer);

        Assert.Equal(0, result.BarsRejected);
        Assert.Empty(observer.RejectedBars);
    }

    [Fact]
    public void RegimeEngineWarmup_ProducesInvalidEvidenceForEarlyBars_NeverTreatedAsAnError()
    {
        // The first bars have Adf/Kpss/etc. still in RegimeEngine's own internal warmup (IsValid=false) -
        // this is a normal, expected condition, not a rejected bar (brief §13: "les barres de warmup...
        // ne doivent pas être silencieusement considérées comme des signaux valides", i.e. they must be
        // visible as warmup, never as an error).
        BacktestScenario scenario = ScenarioOverBars(40);
        var observer = new CapturingBarObserver();

        new BacktestEngine().Run(scenario, warmupBars: 30, observer);

        var firstBar = observer.ProcessedBars[0];
        Assert.True(firstBar.IsWarmup);
        Assert.False(firstBar.Evidence.Adf?.IsValid ?? false);
        Assert.Empty(observer.RejectedBars);
    }
}
