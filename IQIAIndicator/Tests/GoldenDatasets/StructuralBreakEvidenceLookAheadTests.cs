using System;
using System.Linq;
using IQIAIndicator.Backtest;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Regime.Evidence.BaiPerron;
using IQIAIndicator.Engine.Regime.Evidence.CUSUM;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Tests.BacktestTests;
using Xunit;

namespace IQIAIndicator.Tests.GoldenDatasets;

/// <summary>
/// Sprint 15.25 (Lot 15.2). AUDIT-ONLY. Look-ahead, determinism, and run-isolation proofs specifically for
/// <see cref="CusumEvidence"/>/<see cref="BaiPerronEvidence"/> - the two evidence producers this lot audits.
///
/// Three independent, complementary proofs, from the lowest level to the highest:
///   1) Unit-level (CusumEvidence_and_BaiPerronEvidence_OnlyReadTheFirstSampleSizeElements_*): calls
///      Compute() directly with an EvidenceContext.Series array that is LONGER than SampleSize, where
///      everything past index SampleSize-1 is "the future" - Compute()'s own implementation
///      (`for (int i = 0; i &lt; context.SampleSize; i++) series[i] = context.Series[i];`) provably never
///      reads those extra elements, proven here by literally perturbing them and observing no change.
///   2) EvidenceContext-slicing-level (SamePrefix...): reproduces RegimeEngine.BuildEvidenceContext's exact
///      slicing convention (a manual re-derivation, read directly from Engine/Regime/RegimeEngine.cs) over
///      two full price arrays identical up to bar t and diverging after - the standard "same prefix,
///      different future" methodology (brief §5/§14).
///   3) Full-pipeline level (AppendingFutureBars...): runs the REAL, unmodified BacktestEngine.Run() (same
///      technique as the existing Tests/Backtest/BacktestFoundationLookAheadTests.cs, which this file
///      patterns off of directly) over a truncated series and an extended series, comparing every
///      Cusum/BaiPerron field - including Confidence, which the existing reference test does not compare
///      field-by-field (it only compares IsValid/ChangeDetected for Cusum and IsValid/BreakCount/
///      Breakpoints for BaiPerron) - Confidence is the field this lot's ablation harness centrally uses,
///      so its own look-ahead safety is checked explicitly here, not merely assumed from the sibling
///      fields' proof.
///
/// Section 3 of this lot's brief (determinism + run isolation) is also covered here (bottom of file).
/// </summary>
public sealed class StructuralBreakEvidenceLookAheadTests
{
    private const int CusumWindowSize = 30;
    private const int CusumMinimumSampleSize = 20;
    private const int BaiPerronWindowSize = 128;
    private const int BaiPerronMinimumSampleSize = 64;

    // ═══════════════════════ 1) Unit-level: Compute() never reads past SampleSize ═══════════════════════

    [Fact]
    public void CusumEvidence_OnlyReadsTheFirstSampleSizeElements_OfContextSeries()
    {
        decimal[] prefix = SyntheticSeriesCatalog.WhiteNoise(CusumWindowSize, seed: 42UL);

        decimal[] withBenignFuture = prefix.Concat(SyntheticSeriesCatalog.WhiteNoise(20, seed: 1UL)).ToArray();
        decimal[] withWildFuture = prefix.Concat(SyntheticSeriesCatalog.WhiteNoise(20, seed: 2UL).Select(v => v * 1000m)).ToArray();

        CusumResult baseline = RunCusum(prefix, prefix.Length);
        CusumResult benign = RunCusum(withBenignFuture, prefix.Length);
        CusumResult wild = RunCusum(withWildFuture, prefix.Length);

        AssertCusumIdentical(baseline, benign);
        AssertCusumIdentical(baseline, wild);
    }

    [Fact]
    public void BaiPerronEvidence_OnlyReadsTheFirstSampleSizeElements_OfContextSeries()
    {
        decimal[] prefix = SyntheticSeriesCatalog.WhiteNoise(BaiPerronWindowSize, seed: 42UL);

        decimal[] withBenignFuture = prefix.Concat(SyntheticSeriesCatalog.WhiteNoise(40, seed: 1UL)).ToArray();
        decimal[] withWildFuture = prefix.Concat(SyntheticSeriesCatalog.WhiteNoise(40, seed: 2UL).Select(v => v * 1000m)).ToArray();

        BaiPerronResult baseline = RunBaiPerron(prefix, prefix.Length);
        BaiPerronResult benign = RunBaiPerron(withBenignFuture, prefix.Length);
        BaiPerronResult wild = RunBaiPerron(withWildFuture, prefix.Length);

        AssertBaiPerronIdentical(baseline, benign);
        AssertBaiPerronIdentical(baseline, wild);
    }

    // ═══════════════ 2) EvidenceContext-slicing-level: same prefix, different future ══════════════════

    /// <summary>Manual re-derivation of RegimeEngine.BuildEvidenceContext's exact slicing convention
    /// (Engine/Regime/RegimeEngine.cs: series[i] = ring-buffer value at (head - sampleSize + i), i.e. the
    /// last `sampleSize` prices ending at and including bar `t`, oldest-first) - applied here to a plain,
    /// non-ring-buffer chronological array for test clarity.</summary>
    private static decimal[] SliceWindowEndingAt(decimal[] prices, int t, int windowSize)
    {
        int sampleSize = Math.Min(t + 1, windowSize);
        int start = t - sampleSize + 1;
        var window = new decimal[sampleSize];
        for (int i = 0; i < sampleSize; i++)
            window[i] = prices[start + i];
        return window;
    }

    [Fact]
    public void CusumEvidence_SamePrefixDifferentFuture_ProducesIdenticalOutputAtT()
    {
        const int t = 150;
        decimal[] pricesA = SyntheticSeriesCatalog.WhiteNoise(200, seed: 42UL);
        decimal[] pricesB = (decimal[])pricesA.Clone();
        decimal[] futureTail = SyntheticSeriesCatalog.WhiteNoise(49, seed: 999UL); // diverges after t (indices 151..199)
        for (int i = t + 1; i < pricesB.Length; i++)
            pricesB[i] = futureTail[i - (t + 1)];

        decimal[] windowA = SliceWindowEndingAt(pricesA, t, CusumWindowSize);
        decimal[] windowB = SliceWindowEndingAt(pricesB, t, CusumWindowSize);
        Assert.Equal(windowA, windowB); // sanity: the slicing convention itself must not reach past t

        CusumResult resultA = RunCusum(windowA, windowA.Length);
        CusumResult resultB = RunCusum(windowB, windowB.Length);
        AssertCusumIdentical(resultA, resultB);
    }

    [Fact]
    public void BaiPerronEvidence_SamePrefixDifferentFuture_ProducesIdenticalOutputAtT()
    {
        const int t = 150;
        decimal[] pricesA = SyntheticSeriesCatalog.WhiteNoise(200, seed: 42UL);
        decimal[] pricesB = (decimal[])pricesA.Clone();
        decimal[] futureTail = SyntheticSeriesCatalog.WhiteNoise(49, seed: 999UL);
        for (int i = t + 1; i < pricesB.Length; i++)
            pricesB[i] = futureTail[i - (t + 1)];

        decimal[] windowA = SliceWindowEndingAt(pricesA, t, BaiPerronWindowSize);
        decimal[] windowB = SliceWindowEndingAt(pricesB, t, BaiPerronWindowSize);
        Assert.Equal(windowA, windowB);

        BaiPerronResult resultA = RunBaiPerron(windowA, windowA.Length);
        BaiPerronResult resultB = RunBaiPerron(windowB, windowB.Length);
        AssertBaiPerronIdentical(resultA, resultB);
    }

    // ═══════════════════ 3) Full-pipeline level: real BacktestEngine, truncated vs extended ══════════════

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("ES", TickSize: 0.25m, TickValue: 12.5m, PointValue: 50m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    private static CapturingBarObserver RunOver(HistoricalSeries series, int warmupBars)
    {
        var window = new BacktestWindow("FULL", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1));
        BacktestScenario scenario = BacktestScenario.Create(series, window, 50_000m, Spec(), Policy());
        var observer = new CapturingBarObserver();
        new BacktestEngine().Run(scenario, warmupBars, observer);
        return observer;
    }

    [Fact]
    public void AppendingFutureBars_NeverChangesAnyEarlierBarsCusumOrBaiPerronField_IncludingConfidence()
    {
        const int warmupBars = 128;
        const int N = 260;
        const int M = 180;

        HistoricalSeries full = BacktestTestSeriesBuilder.MeanRevertingOu(N, seed: 7UL);
        HistoricalSeries truncated = BacktestTestSeriesBuilder.Truncate(full, M);

        CapturingBarObserver runFull = RunOver(full, warmupBars);
        CapturingBarObserver runTruncated = RunOver(truncated, warmupBars);

        Assert.True(runFull.ProcessedBars.Count >= M);
        Assert.Equal(M, runTruncated.ProcessedBars.Count);

        int mismatches = 0;
        for (int i = 0; i < M; i++)
        {
            CusumResult? cusumFull = runFull.ProcessedBars[i].Evidence.Cusum;
            CusumResult? cusumTruncated = runTruncated.ProcessedBars[i].Evidence.Cusum;
            BaiPerronResult? bpFull = runFull.ProcessedBars[i].Evidence.BaiPerron;
            BaiPerronResult? bpTruncated = runTruncated.ProcessedBars[i].Evidence.BaiPerron;

            bool same =
                cusumFull?.IsValid == cusumTruncated?.IsValid &&
                cusumFull?.ChangeDetected == cusumTruncated?.ChangeDetected &&
                cusumFull?.EstimatedBreakIndex == cusumTruncated?.EstimatedBreakIndex &&
                cusumFull?.PositiveCusum == cusumTruncated?.PositiveCusum &&
                cusumFull?.NegativeCusum == cusumTruncated?.NegativeCusum &&
                cusumFull?.Threshold == cusumTruncated?.Threshold &&
                cusumFull?.Confidence == cusumTruncated?.Confidence &&
                cusumFull?.SampleSize == cusumTruncated?.SampleSize &&
                bpFull?.IsValid == bpTruncated?.IsValid &&
                bpFull?.BreakCount == bpTruncated?.BreakCount &&
                bpFull?.Confidence == bpTruncated?.Confidence &&
                bpFull?.GlobalRSS == bpTruncated?.GlobalRSS &&
                bpFull?.BicScore == bpTruncated?.BicScore &&
                bpFull?.SampleSize == bpTruncated?.SampleSize &&
                (bpFull?.Breakpoints ?? Array.Empty<int>()).SequenceEqual(bpTruncated?.Breakpoints ?? Array.Empty<int>());

            if (!same)
            {
                mismatches++;
                Assert.Fail(
                    $"Bar {i}: appending future bars changed a Cusum/BaiPerron field (look-ahead). " +
                    $"Cusum: full Confidence={cusumFull?.Confidence:F6} vs truncated={cusumTruncated?.Confidence:F6}. " +
                    $"BaiPerron: full Confidence={bpFull?.Confidence:F6} vs truncated={bpTruncated?.Confidence:F6}.");
            }
        }

        Assert.Equal(0, mismatches);
    }

    // ══════════════════════════════ Section 3: determinism + run isolation ════════════════════════════

    [Fact]
    public void Determinism_SameWindowComputedTwice_ProducesBitIdenticalResults()
    {
        decimal[] cusumSeries = SyntheticSeriesCatalog.StructuralBreak(CusumWindowSize, seed: 42UL, shift: 3m);
        decimal[] bpSeries = SyntheticSeriesCatalog.StructuralBreak(BaiPerronWindowSize, seed: 42UL, shift: 3m);

        CusumResult c1 = RunCusum(cusumSeries, cusumSeries.Length);
        CusumResult c2 = RunCusum(cusumSeries, cusumSeries.Length);
        AssertCusumIdentical(c1, c2);

        BaiPerronResult b1 = RunBaiPerron(bpSeries, bpSeries.Length);
        BaiPerronResult b2 = RunBaiPerron(bpSeries, bpSeries.Length);
        AssertBaiPerronIdentical(b1, b2);
    }

    /// <summary>CusumEvidence/BaiPerronEvidence/CusumStatistics/BaiPerronStatistics carry no instance or
    /// static mutable field (confirmed by inspection of Engine/Regime/Evidence/CUSUM and .../BaiPerron -
    /// Compute() takes everything via its EvidenceContext/series parameter and uses only local variables
    /// and freshly-allocated arrays). This test verifies that behaviorally: interleaving two independent
    /// scenarios (A, B, A) must leave A's second result identical to A's first - if either class secretly
    /// carried shared state, running B in between would perturb it.</summary>
    [Fact]
    public void RunIsolation_InterleavingTwoIndependentScenarios_NeverPerturbsTheFirst()
    {
        var cusumEvidence = new CusumEvidence();
        var baiPerronEvidence = new BaiPerronEvidence();

        decimal[] scenarioA = SyntheticSeriesCatalog.StructuralBreak(CusumWindowSize, seed: 42UL, shift: 3m);
        decimal[] scenarioB = SyntheticSeriesCatalog.VarianceBreak(CusumWindowSize, seed: 7UL);
        decimal[] scenarioABp = SyntheticSeriesCatalog.StructuralBreak(BaiPerronWindowSize, seed: 42UL, shift: 3m);
        decimal[] scenarioBBp = SyntheticSeriesCatalog.VarianceBreak(BaiPerronWindowSize, seed: 7UL);

        CusumResult a1 = cusumEvidence.Compute(ContextFor(scenarioA, CusumMinimumSampleSize, CusumWindowSize));
        BaiPerronResult a1Bp = baiPerronEvidence.Compute(ContextFor(scenarioABp, BaiPerronMinimumSampleSize, BaiPerronWindowSize));

        _ = cusumEvidence.Compute(ContextFor(scenarioB, CusumMinimumSampleSize, CusumWindowSize));
        _ = baiPerronEvidence.Compute(ContextFor(scenarioBBp, BaiPerronMinimumSampleSize, BaiPerronWindowSize));

        CusumResult a2 = cusumEvidence.Compute(ContextFor(scenarioA, CusumMinimumSampleSize, CusumWindowSize));
        BaiPerronResult a2Bp = baiPerronEvidence.Compute(ContextFor(scenarioABp, BaiPerronMinimumSampleSize, BaiPerronWindowSize));

        AssertCusumIdentical(a1, a2);
        AssertBaiPerronIdentical(a1Bp, a2Bp);
    }

    // ──────────────────────────────────────────────── Helpers ─────────────────────────────────────────────

    private static EvidenceContext ContextFor(decimal[] series, int minimumSampleSize, int windowSize) => new()
    {
        Series = series,
        SampleSize = series.Length,
        MinimumSampleSize = minimumSampleSize,
        WindowSize = windowSize,
        Timestamp = DateTime.UnixEpoch
    };

    private static CusumResult RunCusum(decimal[] series, int sampleSize) => new CusumEvidence().Compute(new EvidenceContext
    {
        Series = series,
        SampleSize = sampleSize,
        MinimumSampleSize = CusumMinimumSampleSize,
        WindowSize = CusumWindowSize,
        Timestamp = DateTime.UnixEpoch
    });

    private static BaiPerronResult RunBaiPerron(decimal[] series, int sampleSize) => new BaiPerronEvidence().Compute(new EvidenceContext
    {
        Series = series,
        SampleSize = sampleSize,
        MinimumSampleSize = BaiPerronMinimumSampleSize,
        WindowSize = BaiPerronWindowSize,
        Timestamp = DateTime.UnixEpoch
    });

    private static void AssertCusumIdentical(CusumResult expected, CusumResult actual)
    {
        Assert.Equal(expected.IsValid, actual.IsValid);
        Assert.Equal(expected.ChangeDetected, actual.ChangeDetected);
        Assert.Equal(expected.EstimatedBreakIndex, actual.EstimatedBreakIndex);
        Assert.Equal(expected.PositiveCusum, actual.PositiveCusum);
        Assert.Equal(expected.NegativeCusum, actual.NegativeCusum);
        Assert.Equal(expected.Threshold, actual.Threshold);
        Assert.Equal(expected.Confidence, actual.Confidence);
        Assert.Equal(expected.SampleSize, actual.SampleSize);
        Assert.Equal(expected.Explanation, actual.Explanation);
    }

    private static void AssertBaiPerronIdentical(BaiPerronResult expected, BaiPerronResult actual)
    {
        Assert.Equal(expected.IsValid, actual.IsValid);
        Assert.Equal(expected.BreakCount, actual.BreakCount);
        Assert.Equal(expected.Breakpoints, actual.Breakpoints);
        Assert.Equal(expected.Confidence, actual.Confidence);
        Assert.Equal(expected.GlobalRSS, actual.GlobalRSS);
        Assert.Equal(expected.BicScore, actual.BicScore);
        Assert.Equal(expected.SampleSize, actual.SampleSize);
        Assert.Equal(expected.Explanation, actual.Explanation);
    }
}
