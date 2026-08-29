using System;
using IQIAIndicator.Backtest;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Tests.BacktestTests;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Pipeline;

/// <summary>
/// Sprint 15.25 (Lot 14.3, brief §22 - "test extrêmement important", BLOCKING acceptance criterion).
/// Dataset B = Dataset A + future bars appended. For every bar common to both, every decision-relevant
/// pipeline output must be IDENTICAL between the two runs - appending future data must never change a
/// decision already produced for an earlier bar.
///
/// Scope note: this test deliberately does NOT re-compare raw <see cref="Engine.Regime.Core.EvidenceSet"/>
/// field-by-field - Lot 14.1's own BacktestFoundationLookAheadTests already proved that exhaustively at
/// the Regime layer, and <see cref="BacktestEngine.RunSignalPipeline"/> reuses the exact same
/// <c>BuildValidatedContext</c> + <c>RegimeEngine.Collect</c> call path <c>Run()</c> does (verified by the
/// Lot 14.3 report's extraction diff) - re-deriving that proof here would be redundant. This test instead
/// covers the NEW layers this lot wires in: Fusion -&gt; Decision -&gt; Methodology -&gt; Signal -&gt; Entry -&gt;
/// EntryTrigger -&gt; TradePlan.
/// </summary>
public sealed class BacktestSignalPipelineLookAheadTests
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

    [Fact]
    public void AppendingFutureBars_NeverChangesAnyDecisionAlreadyProducedForAnEarlierBar()
    {
        // ONE long series, then Truncate() for the short run - guarantees a byte-identical prefix,
        // rather than relying on two independently-seeded generators happening to agree (the same
        // technique Lot 14.1's own look-ahead test already used).
        HistoricalSeries full = BacktestTestSeriesBuilder.MeanRevertingOu(320, seed: 13UL);
        HistoricalSeries truncated = BacktestTestSeriesBuilder.Truncate(full, 220);

        const int warmupBars = 128;
        BacktestSignalPipelineResult shortRun = new BacktestEngine().RunSignalPipeline(ScenarioFor(truncated), warmupBars);
        BacktestSignalPipelineResult longRun = new BacktestEngine().RunSignalPipeline(ScenarioFor(full), warmupBars);

        Assert.Equal(220, shortRun.Bars.Count);
        Assert.True(longRun.Bars.Count > 220);

        int mismatches = 0;
        for (int i = 0; i < 220; i++)
        {
            BacktestSignalResult s = shortRun.Bars[i];
            BacktestSignalResult l = longRun.Bars[i];

            bool same =
                s.Status == l.Status &&
                s.Timestamp == l.Timestamp &&
                s.Decision?.Winner == l.Decision?.Winner &&
                s.Decision?.AmbiguityScore == l.Decision?.AmbiguityScore &&
                s.Decision?.WinnerScore == l.Decision?.WinnerScore &&
                s.Decision?.State == l.Decision?.State &&
                s.Decision?.Confidence == l.Decision?.Confidence &&
                s.Decision?.Candidates.Length == l.Decision?.Candidates.Length &&
                s.Methodology?.SelectedMethodology.Name == l.Methodology?.SelectedMethodology.Name &&
                s.Entry?.OpportunityStatus == l.Entry?.OpportunityStatus &&
                s.Entry?.OpportunityPriority == l.Entry?.OpportunityPriority &&
                s.EntryTrigger?.Assessment.Direction == l.EntryTrigger?.Assessment.Direction &&
                s.EntryTrigger?.Assessment.TriggerStatus == l.EntryTrigger?.Assessment.TriggerStatus &&
                s.EntryTrigger?.Assessment.Reason == l.EntryTrigger?.Assessment.Reason &&
                s.EntryTrigger?.Assessment.ScientificConfidence == l.EntryTrigger?.Assessment.ScientificConfidence &&
                s.EntryTrigger?.Assessment.EstimatedEquilibrium == l.EntryTrigger?.Assessment.EstimatedEquilibrium &&
                s.EntryTrigger?.Assessment.DistanceToEquilibrium == l.EntryTrigger?.Assessment.DistanceToEquilibrium &&
                s.EntryTrigger?.CurrentPrice == l.EntryTrigger?.CurrentPrice &&
                s.TradePlan?.Status == l.TradePlan?.Status &&
                s.TradePlan?.Direction == l.TradePlan?.Direction &&
                s.TradePlan?.EntryPrice == l.TradePlan?.EntryPrice &&
                s.TradePlan?.StopLoss == l.TradePlan?.StopLoss &&
                s.TradePlan?.TakeProfit == l.TradePlan?.TakeProfit &&
                s.TradePlan?.RiskPerUnit == l.TradePlan?.RiskPerUnit &&
                s.TradePlan?.PositionSize == l.TradePlan?.PositionSize &&
                s.TradePlan?.RiskRewardRatio == l.TradePlan?.RiskRewardRatio;

            if (!same)
            {
                mismatches++;
                Assert.Fail(
                    $"Bar {i}: appending future data changed the pipeline outcome (look-ahead). " +
                    $"Short: Decision={s.Decision?.Winner}/{s.Decision?.AmbiguityScore:F6}, Direction={s.EntryTrigger?.Assessment.Direction}, TradePlan={s.TradePlan?.Status}. " +
                    $"Long: Decision={l.Decision?.Winner}/{l.Decision?.AmbiguityScore:F6}, Direction={l.EntryTrigger?.Assessment.Direction}, TradePlan={l.TradePlan?.Status}.");
            }
        }

        Assert.Equal(0, mismatches);
    }

    [Fact]
    public void ShorterRun_NeverThrowsAndNeverRejectsMoreBarsThanTheLongerRun_OverTheSharedPrefix()
    {
        HistoricalSeries full = BacktestTestSeriesBuilder.WhiteNoise(200, seed: 55UL);
        HistoricalSeries truncated = BacktestTestSeriesBuilder.Truncate(full, 90);

        BacktestSignalPipelineResult shortRun = new BacktestEngine().RunSignalPipeline(ScenarioFor(truncated), warmupBars: 128);
        BacktestSignalPipelineResult longRun = new BacktestEngine().RunSignalPipeline(ScenarioFor(full), warmupBars: 128);

        Assert.Equal(0, shortRun.ExceptionCount);
        Assert.Equal(0, shortRun.BarsRejected);
        for (int i = 0; i < 90; i++)
            Assert.Equal(longRun.Bars[i].BarIndex, shortRun.Bars[i].BarIndex);
    }
}
