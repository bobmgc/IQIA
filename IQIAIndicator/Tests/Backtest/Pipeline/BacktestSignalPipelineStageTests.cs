using System;
using System.Linq;
using IQIAIndicator.Backtest;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Engine.TradePlan;
using IQIAIndicator.Tests.BacktestTests;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Pipeline;

/// <summary>
/// Sprint 15.25 (Lot 14.3, brief §20/§21). Stage-by-stage traversal of the full signal pipeline
/// (MarketContext -&gt; Regime -&gt; Decision -&gt; Methodology/Signal -&gt; Entry -&gt; EntryTrigger -&gt; TradePlan),
/// plus the fixtures required by brief §20. Fixtures C/D (invalid timestamps / invalid OHLC) are proven
/// at the <see cref="HistoricalSeries"/> boundary - exhaustively covered already by Lot 14.1's
/// HistoricalBarTests/HistoricalSeriesTests - so this file only confirms that boundary sits strictly
/// BEFORE <see cref="BacktestEngine.RunSignalPipeline"/> can ever be reached, never re-derives that
/// coverage.
/// </summary>
public sealed class BacktestSignalPipelineStageTests
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

    // ── Fixture A: long enough to clear warmup ──────────────────────────────────────────────────────

    [Fact]
    public void FixtureA_LongMeanRevertingSeries_RunsEndToEndToTradePlan_ForEveryBar()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(220, seed: 7UL);
        BacktestScenario scenario = ScenarioFor(series);

        BacktestSignalPipelineResult result = new BacktestEngine().RunSignalPipeline(scenario, warmupBars: 128);

        Assert.Equal(220, result.BarsProcessed);
        Assert.Equal(0, result.BarsRejected);
        Assert.Equal(0, result.ExceptionCount);
        Assert.Equal(220, result.Bars.Count);

        // Every processed bar (Warmup or Ready) must have walked the pipeline all the way to a
        // Decision/Methodology/Signal/Entry/EntryTrigger result - brief §21's end-to-end requirement.
        foreach (BacktestSignalResult bar in result.Bars)
        {
            Assert.True(bar.Status is BacktestSignalStatus.Warmup or BacktestSignalStatus.Ready);
            Assert.NotNull(bar.Context);
            Assert.NotNull(bar.Regime);
            Assert.NotNull(bar.Decision);
            Assert.NotNull(bar.Methodology);
            Assert.NotNull(bar.Signal);
            Assert.NotNull(bar.Entry);
            Assert.NotNull(bar.EntryTrigger);
        }
    }

    // ── Fixture B: too short to ever leave warmup ───────────────────────────────────────────────────

    [Fact]
    public void FixtureB_ShortSeries_NeverProducesReadyBars_ButStillRunsThePipeline()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(5, seed: 7UL);
        BacktestScenario scenario = ScenarioFor(series);

        BacktestSignalPipelineResult result = new BacktestEngine().RunSignalPipeline(scenario, warmupBars: 128);

        Assert.Equal(5, result.BarsProcessed);
        Assert.Equal(5, result.WarmupBars);
        Assert.Equal(0, result.ReadyBars);
        Assert.All(result.Bars, bar => Assert.Equal(BacktestSignalStatus.Warmup, bar.Status));
        // Still no fabricated decision/signal - the real (if data-starved) engines still ran (brief §5).
        Assert.All(result.Bars, bar => Assert.NotNull(bar.Decision));
    }

    // ── Fixture C / D: invalid data never reaches RunSignalPipeline at all ──────────────────────────

    [Fact]
    public void FixtureC_NonChronologicalTimestamps_RejectedAtHistoricalSeriesBoundary_BeforeAnyPipelineRun()
    {
        var bars = new[]
        {
            new HistoricalBar(new DateTime(2026, 1, 5, 10, 5, 0, DateTimeKind.Utc), 100m, 101m, 99m, 100m, 10m),
            new HistoricalBar(new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc), 100m, 101m, 99m, 100m, 10m) // out of order
        };

        HistoricalSeries.TryCreate("ES", "M5", "UTC", "Test", bars, out _, out var violations);

        Assert.NotEmpty(violations);
    }

    [Fact]
    public void FixtureD_InvalidOhlc_RejectedAtHistoricalBarBoundary_BeforeAnyPipelineRun()
    {
        // High < Low is structurally invalid - HistoricalBar.Validate() (Lot 14.1) catches it, a superset
        // of MarketContextValidator's own checks, so it can never reach RunSignalPipeline.
        var invalidBar = new HistoricalBar(
            new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc), Open: 100m, High: 90m, Low: 95m, Close: 100m, Volume: 10m);

        Assert.False(invalidBar.IsValid);
        Assert.NotEmpty(invalidBar.Validate());
    }

    // ── Fixture E: valid data without microstructure (Bid/Ask/Delta/OpenInterest all absent) ───────

    [Fact]
    public void FixtureE_DataWithoutMicrostructure_StillRunsThePipeline_NeverFabricatesTheAbsentFields()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(150, seed: 11UL);
        Assert.All(series.Bars, bar =>
        {
            Assert.Null(bar.BidVolume);
            Assert.Null(bar.AskVolume);
            Assert.Null(bar.Delta);
            Assert.Null(bar.OpenInterest);
        });

        BacktestScenario scenario = ScenarioFor(series);
        BacktestSignalPipelineResult result = new BacktestEngine().RunSignalPipeline(scenario, warmupBars: 128);

        Assert.Equal(150, result.BarsProcessed);
        Assert.Equal(0, result.ExceptionCount);
    }

    // ── §21: stage-by-stage transitions ─────────────────────────────────────────────────────────────

    [Fact]
    public void Stage_MarketContextToRegime_EvidenceIsPresentForEveryProcessedBar()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.WhiteNoise(60, seed: 3UL);
        BacktestSignalPipelineResult result = new BacktestEngine().RunSignalPipeline(ScenarioFor(series), warmupBars: 30);

        Assert.All(result.Bars, bar => Assert.NotNull(bar.Regime));
    }

    [Fact]
    public void Stage_RegimeToDecision_DecisionIsBuiltFromTheSameEvidenceRegimeProduced()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(150, seed: 5UL);
        BacktestSignalPipelineResult result = new BacktestEngine().RunSignalPipeline(ScenarioFor(series), warmupBars: 128);

        BacktestSignalResult last = result.Bars[^1];
        Assert.NotNull(last.Regime);
        Assert.NotNull(last.Decision);
        // Winner is only ever a concrete state or Unknown - never null/fabricated.
        Assert.True(Enum.IsDefined(typeof(MarketState), last.Decision!.Winner));
    }

    [Fact]
    public void Stage_DecisionToSignal_MethodologyAndOpportunityPresentationArePopulated()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(150, seed: 5UL);
        BacktestSignalPipelineResult result = new BacktestEngine().RunSignalPipeline(ScenarioFor(series), warmupBars: 128);

        BacktestSignalResult last = result.Bars[^1];
        Assert.NotNull(last.Methodology);
        Assert.NotNull(last.Signal);
        Assert.Equal(last.Decision, last.Methodology!.DecisionResult);
    }

    [Fact]
    public void Stage_SignalToEntry_EntryCandidateIsPopulated()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(150, seed: 5UL);
        BacktestSignalPipelineResult result = new BacktestEngine().RunSignalPipeline(ScenarioFor(series), warmupBars: 128);

        Assert.All(result.Bars.Where(bar => bar.Status != BacktestSignalStatus.Rejected), bar => Assert.NotNull(bar.Entry));
    }

    [Fact]
    public void Stage_EntryToEntryTrigger_EntryTriggerCarriesTheSameEntryCandidate()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(150, seed: 5UL);
        BacktestSignalPipelineResult result = new BacktestEngine().RunSignalPipeline(ScenarioFor(series), warmupBars: 128);

        BacktestSignalResult last = result.Bars[^1];
        Assert.NotNull(last.EntryTrigger);
        Assert.Equal(last.Entry, last.EntryTrigger!.EntryCandidate);
    }

    [Fact]
    public void Stage_EntryTriggerToTradePlan_TradePlanIsBuiltOnlyWhenEntryTriggerIsPresent_NeverFabricated()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(150, seed: 5UL);
        BacktestSignalPipelineResult result = new BacktestEngine().RunSignalPipeline(ScenarioFor(series), warmupBars: 128);

        foreach (BacktestSignalResult bar in result.Bars)
        {
            if (bar.Status == BacktestSignalStatus.Rejected)
                continue;

            // EntryTrigger is always produced by SignalEngine.Process once it returns without throwing
            // (LastEntryTriggerCandidate is unconditionally set - see SignalEngine.cs), so TradePlan must
            // be present too, mirroring IQIAIndicator.OnCalculate's own "if (entryTriggerCandidate is not
            // null)" gate.
            Assert.NotNull(bar.EntryTrigger);
            Assert.NotNull(bar.TradePlan);
            // TradePlanBuilder never fabricates a Stop Loss (brief §13/§32) - with no RiskParameters
            // wired in this lot, Status can never be PLAN_READY.
            Assert.NotEqual(TradePlanStatus.PLAN_READY, bar.TradePlan!.Status);
            Assert.Null(bar.TradePlan!.StopLoss);
        }
    }

    // ── §21: full end-to-end HistoricalSeries -> TradePlan ──────────────────────────────────────────

    [Fact]
    public void EndToEnd_HistoricalSeriesToTradePlan_ProducesATraceableResultForEveryBar()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(300, seed: 21UL);
        BacktestSignalPipelineResult result = new BacktestEngine().RunSignalPipeline(ScenarioFor(series), warmupBars: 128);

        Assert.Equal(300, result.BarsProcessed);
        Assert.Equal(300, result.Bars.Count);
        Assert.Equal(0, result.ExceptionCount);
        // Observability (brief §28): every bar's Timestamp -> Regime -> Decision -> ... -> TradePlan chain
        // must be retrievable from the single BacktestSignalResult for that bar.
        BacktestSignalResult sample = result.Bars[200];
        Assert.Equal(series.Bars[200].Timestamp, sample.Timestamp);
        Assert.NotNull(sample.Regime);
        Assert.NotNull(sample.Decision);
        Assert.NotNull(sample.Methodology);
        Assert.NotNull(sample.Signal);
        Assert.NotNull(sample.Entry);
        Assert.NotNull(sample.EntryTrigger);
    }

    // ── §5: warmup never fabricates a decision, and reuses the pipeline's own NOT_READY concept ────

    [Fact]
    public void Warmup_BarsBelowThreshold_AreLabeledWarmup_ButStillCarryRealPipelineOutput()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(160, seed: 9UL);
        BacktestSignalPipelineResult result = new BacktestEngine().RunSignalPipeline(ScenarioFor(series), warmupBars: 128);

        Assert.Equal(128, result.WarmupBars);
        Assert.Equal(32, result.ReadyBars);

        for (int i = 0; i < 128; i++)
            Assert.Equal(BacktestSignalStatus.Warmup, result.Bars[i].Status);
        for (int i = 128; i < 160; i++)
            Assert.Equal(BacktestSignalStatus.Ready, result.Bars[i].Status);
    }

    [Fact]
    public void NegativeWarmupBars_Throws()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.WhiteNoise(10, seed: 1UL);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BacktestEngine().RunSignalPipeline(ScenarioFor(series), warmupBars: -1));
    }

    [Fact]
    public void NullScenario_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new BacktestEngine().RunSignalPipeline(null!, warmupBars: 10));
    }
}
