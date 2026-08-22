using System;
using System.Linq;
using IQIAIndicator.Backtest;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Risk;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests;

/// <summary>
/// Sprint 15.25 (Lot 14.1, brief §15 - MANDATORY). Signal(bar i) must be a function of Bars[0..i] only.
///
/// Method: run the engine once on a full series [0..N), once on a series truncated to [0..M) with
/// M &lt; N, and compare every bar in the common range [0..M) between the two runs. Any difference proves
/// a future bar leaked into a past bar's computation - the exact discipline already applied by the
/// pre-existing StopLossCalibrationPocTests.AssertLookAheadSafety_TruncatingFutureBarsProducesIdenticalMetrics
/// and VolatilityRegimeLookAheadAuditTests, extended here from "one scientific model" to "the whole
/// MarketContext + RegimeEngine.Collect this lot wires up".
///
/// IsLastBar is deliberately EXCLUDED from the field-by-field comparison at exactly one bar (index M-1):
/// it is legitimately true in the short run and false in the long run at that single index (that bar
/// really is the short run's last bar), and MarketClock.IsLastBar is read by nothing this lot computes
/// (RegimeEngine.Collect never reads it - confirmed by inspection of Engine/Regime/RegimeEngine.cs and
/// its evidence models). Every other field, at every bar including index M-1, is compared and must match.
/// </summary>
public sealed class BacktestFoundationLookAheadTests
{
    private const int WarmupBars = 30;

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("ES", TickSize: 0.25m, TickValue: 12.5m, PointValue: 50m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    private static CapturingBarObserver RunOver(HistoricalSeries series)
    {
        var window = new BacktestWindow("FULL", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1));
        BacktestScenario scenario = BacktestScenario.Create(series, window, 50_000m, Spec(), Policy());
        var observer = new CapturingBarObserver();
        new BacktestEngine().Run(scenario, WarmupBars, observer);
        return observer;
    }

    [Fact]
    public void TruncatingFutureBars_ProducesIdenticalContextsAndEvidence_ForEveryCommonBar()
    {
        const int N = 200;
        const int M = 140;

        HistoricalSeries full = BacktestTestSeriesBuilder.WhiteNoise(N);
        HistoricalSeries truncated = BacktestTestSeriesBuilder.Truncate(full, M);

        CapturingBarObserver runA = RunOver(full);
        CapturingBarObserver runB = RunOver(truncated);

        Assert.True(runA.ProcessedBars.Count >= M, "Run A must have processed at least the common range.");
        Assert.Equal(M, runB.ProcessedBars.Count);

        for (int i = 0; i < M; i++)
        {
            var a = runA.ProcessedBars[i];
            var b = runB.ProcessedBars[i];

            Assert.Equal(i, a.Index);
            Assert.Equal(i, b.Index);
            Assert.Equal(a.IsWarmup, b.IsWarmup);

            bool includeIsLastBar = i != M - 1; // see class doc comment
            AssertContextsMatch(a.Context, b.Context, includeIsLastBar);
            AssertEvidenceMatches(a.Evidence, b.Evidence);
        }
    }

    [Fact]
    public void FutureBarPerturbation_NeverChangesAPastBarsContextOrEvidence()
    {
        // Complementary framing of the same property: perturb ONLY the tail of the series (bars that lie
        // strictly after the bar under test) and prove the earlier bar's own context/evidence is
        // untouched. If a future value could leak backward, altering it would change a past result.
        const int N = 200;
        const int cutoff = 140; // bars [0..cutoff) must never react to anything at/after this index

        HistoricalSeries baseline = BacktestTestSeriesBuilder.WhiteNoise(N, seed: 42UL);
        HistoricalSeries perturbed = BacktestTestSeriesBuilder.WhiteNoise(N, seed: 43UL); // different tail via different seed

        // Splice: bars [0..cutoff) identical to baseline, bars [cutoff..N) come from the other seed -
        // this isolates "did changing only the future change the past".
        var splicedBars = new System.Collections.Generic.List<HistoricalBar>(N);
        for (int i = 0; i < N; i++)
            splicedBars.Add(i < cutoff ? baseline.Bars[i] : perturbed.Bars[i]);
        HistoricalSeries spliced = HistoricalSeries.Create(baseline.Symbol, baseline.TimeFrame, baseline.TimeZone, baseline.Provider, splicedBars);

        CapturingBarObserver runBaseline = RunOver(baseline);
        CapturingBarObserver runSpliced = RunOver(spliced);

        for (int i = 0; i < cutoff; i++)
        {
            AssertContextsMatch(runBaseline.ProcessedBars[i].Context, runSpliced.ProcessedBars[i].Context, includeIsLastBar: true);
            AssertEvidenceMatches(runBaseline.ProcessedBars[i].Evidence, runSpliced.ProcessedBars[i].Evidence);
        }
    }

    // ── Independent, explicit field comparison - deliberately NOT reusing BacktestFingerprint, so this
    //    test verifies the engine's real output rather than trusting the same hashing code twice. ──────

    private static void AssertContextsMatch(MarketContext expected, MarketContext actual, bool includeIsLastBar)
    {
        Assert.Equal(expected.BarIndex, actual.BarIndex);
        Assert.Equal(expected.TimeFrame, actual.TimeFrame);
        Assert.Equal(expected.Price, actual.Price);
        Assert.Equal(expected.Volume, actual.Volume);
        Assert.Equal(expected.Instrument, actual.Instrument);
        Assert.Equal(expected.Clock.CurrentTime, actual.Clock.CurrentTime);
        Assert.Equal(expected.Clock.CurrentDate, actual.Clock.CurrentDate);
        Assert.Equal(expected.Clock.DayOfWeek, actual.Clock.DayOfWeek);
        Assert.Equal(expected.Clock.ElapsedMinutes, actual.Clock.ElapsedMinutes);
        Assert.Equal(expected.Clock.IsFirstBar, actual.Clock.IsFirstBar);
        if (includeIsLastBar)
            Assert.Equal(expected.Clock.IsLastBar, actual.Clock.IsLastBar);
        Assert.Equal(expected.Execution.CurrentBar, actual.Execution.CurrentBar);
        Assert.Equal(expected.Execution.LastCalculatedBar, actual.Execution.LastCalculatedBar);
        Assert.Equal(expected.Execution.IsRealtime, actual.Execution.IsRealtime);
        Assert.Equal(expected.Execution.IsHistorical, actual.Execution.IsHistorical);
        Assert.Equal(expected.Execution.IsReplay, actual.Execution.IsReplay);
    }

    private static void AssertEvidenceMatches(EvidenceSet expected, EvidenceSet actual)
    {
        Assert.Equal(expected.Timestamp, actual.Timestamp);

        Assert.Equal(expected.Adf?.IsValid, actual.Adf?.IsValid);
        Assert.Equal(expected.Adf?.Statistic, actual.Adf?.Statistic);

        Assert.Equal(expected.Kpss?.IsValid, actual.Kpss?.IsValid);
        Assert.Equal(expected.Kpss?.Statistic, actual.Kpss?.Statistic);

        Assert.Equal(expected.Hurst?.IsValid, actual.Hurst?.IsValid);
        Assert.Equal(expected.Hurst?.HurstProxy, actual.Hurst?.HurstProxy);

        Assert.Equal(expected.HalfLife?.IsValid, actual.HalfLife?.IsValid);
        Assert.Equal(expected.HalfLife?.HalfLife, actual.HalfLife?.HalfLife);

        Assert.Equal(expected.VarianceRatio?.IsValid, actual.VarianceRatio?.IsValid);
        Assert.Equal(expected.VarianceRatio?.VarianceRatio, actual.VarianceRatio?.VarianceRatio);

        Assert.Equal(expected.Cusum?.IsValid, actual.Cusum?.IsValid);
        Assert.Equal(expected.Cusum?.ChangeDetected, actual.Cusum?.ChangeDetected);

        Assert.Equal(expected.Volatility?.IsValid, actual.Volatility?.IsValid);
        Assert.Equal(expected.Volatility?.AcfAbsReturns, actual.Volatility?.AcfAbsReturns);

        Assert.Equal(expected.BaiPerron?.IsValid, actual.BaiPerron?.IsValid);
        Assert.Equal(expected.BaiPerron?.BreakCount, actual.BaiPerron?.BreakCount);
        Assert.Equal(expected.BaiPerron?.Breakpoints, actual.BaiPerron?.Breakpoints);

        Assert.Equal(expected.Dfa?.IsValid, actual.Dfa?.IsValid);
        Assert.Equal(expected.Dfa?.Hurst, actual.Dfa?.Hurst);
    }
}
