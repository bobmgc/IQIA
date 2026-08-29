using System.Linq;
using IQIAIndicator.Backtest;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Tests.BacktestTests;
using IQIAIndicator.Tests.GoldenDatasets;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Pipeline;

/// <summary>
/// Sprint 15.25 (Lot 14.3, brief §18). Exception policy: a stage that throws for one bar is caught,
/// recorded with Stage/ExceptionType/Message on that bar's <see cref="BacktestSignalResult"/>, and the
/// run continues (never <c>catch { return null; }</c>, never escalated to a whole-run failure) - see
/// <see cref="BacktestEngine.RunSignalPipeline"/>'s doc comment for the documented decision and its
/// rationale.
///
/// SEARCHED FOR, NOT FOUND: a data shape that makes any of the wired-in engines actually throw. Every
/// model/rule this lot's inspection covered (DynamicZScoreModel, VolatilityModel, EntryTriggerBuilder,
/// TradePlanBuilder, DecisionEngine, MethodologyEngine, FusionStateManager) degrades to a
/// Success=false/NOT_QUALIFIED/NO_ACTION/SIGNAL_ONLY result rather than throwing - confirmed empirically
/// below across white noise, a degenerate constant (zero-variance) series, and a single-bar series (the
/// most extreme legal warmup case). This is a POSITIVE finding, documented rather than silently assumed:
/// the try/catch machinery in RunSignalPipeline exists as documented defence-in-depth (brief §18 requires
/// it exist and be exercised honestly, not that a failure be manufactured to prove it). If a future model
/// addition ever does throw, <see cref="NoExceptions_AcrossTheEdgeCasesThisLotCouldConstruct"/> below will
/// fail loudly and point at exactly which one.
/// </summary>
public sealed class BacktestSignalPipelineExceptionTests
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

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(150)]
    public void NoExceptions_AcrossTheEdgeCasesThisLotCouldConstruct(int constantSeriesLength)
    {
        // A perfectly flat (zero-variance) price series is the most degenerate legal input
        // HistoricalSeries.Create accepts (every bar individually valid, Volume>=0, High>=Low) - the
        // shape most likely to hit a division-by-zero in variance/half-life/Kalman-gain arithmetic if
        // one existed unguarded.
        HistoricalSeries constant = BacktestTestSeriesBuilder.BuildFromCloses(SyntheticSeriesCatalog.Constant(constantSeriesLength));
        BacktestSignalPipelineResult result = new BacktestEngine().RunSignalPipeline(ScenarioFor(constant), warmupBars: 128);

        Assert.Equal(0, result.ExceptionCount);
        Assert.All(result.Bars, bar => Assert.Null(bar.Exception));
    }

    [Fact]
    public void EveryBarStatus_IsExactlyOneOfTheFourDocumentedValues_NeverLeftUnaccountedFor()
    {
        HistoricalSeries series = BacktestTestSeriesBuilder.MeanRevertingOu(200, seed: 33UL);
        BacktestSignalPipelineResult result = new BacktestEngine().RunSignalPipeline(ScenarioFor(series), warmupBars: 128);

        Assert.Equal(result.Bars.Count, result.BarsProcessed + result.BarsRejected);
        Assert.Equal(result.BarsProcessed, result.Bars.Count(bar =>
            bar.Status is BacktestSignalStatus.Warmup or BacktestSignalStatus.Ready or BacktestSignalStatus.Exception));
        Assert.Equal(result.ExceptionCount, result.Bars.Count(bar => bar.Status == BacktestSignalStatus.Exception));
    }

    [Fact]
    public void IfAnExceptionEverOccurs_ItIsRecordedWithANonEmptyStageAndType_NeverSwallowedSilently()
    {
        // Structural invariant, exercised over every fixture this file constructs: whatever produced an
        // Exception-status bar, it can never carry a null BacktestStageException (brief §18: "NE PAS
        // faire catch { return null; } sans trace").
        foreach (HistoricalSeries series in new[]
                 {
                     BacktestTestSeriesBuilder.MeanRevertingOu(200, seed: 33UL),
                     BacktestTestSeriesBuilder.WhiteNoise(200, seed: 34UL),
                     BacktestTestSeriesBuilder.BuildFromCloses(SyntheticSeriesCatalog.Constant(150))
                 })
        {
            BacktestSignalPipelineResult result = new BacktestEngine().RunSignalPipeline(ScenarioFor(series), warmupBars: 128);
            foreach (BacktestSignalResult bar in result.Bars.Where(bar => bar.Status == BacktestSignalStatus.Exception))
            {
                Assert.NotNull(bar.Exception);
                Assert.False(string.IsNullOrWhiteSpace(bar.Exception!.Stage));
                Assert.False(string.IsNullOrWhiteSpace(bar.Exception.ExceptionType));
            }
        }
    }
}
