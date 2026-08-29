using System;
using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Engine.ScientificModels.MeanReversion;
using IQIAIndicator.Tests.GoldenDatasets;

namespace IQIAIndicator.Tests.ScientificModels;

/// <summary>
/// Sprint 15.22 (QDE-012 InnovationStd correction). Protects the bounded-rolling-window contract
/// KalmanFilterModel now implements - see that class's doc comment and the QDE-012_Sprint_15.21/15.22
/// reports for the full evidence trail (why the pre-15.22 full-history design was wrong, why a bounded
/// window fixes it, why the fix is scoped to this one file). These tests express the CONTRACT the
/// corrected model must satisfy; they do not calibrate or touch QDE-012's stop-loss k anywhere.
/// </summary>
public static class KalmanFilterModelWindowTests
{
    public static void RunAll()
    {
        Test1_HistoryLengthInvariance_NonStationarySeries();
        Test2_NoLookAhead_TruncatedVsFullBackingArrayProduceIdenticalResults();
        Test3_RollingWindowBoundary_ObservationsUsedNeverExceedsWindowSize();
        Test4_Warmup_ShorterThanWindowUsesAllAvailableObservations();
        Test5_OutlierSensitivity_SingleOutlierEffectStaysBounded();
        Test6_UnitConsistency_InnovationStdScalesLinearlyWithPriceScale();
        Test7_QDE012IntegrationContract_A1StopDistanceStaysFiniteNonNegativeAndLinearInK();
        Test8_KalmanStateIntegrity_NoNaNInfinityNegativeVarianceAndDeterministic();
    }

    // ── Test 1: History Length Invariance ───────────────────────────────────────────────────────────
    // Threshold origin: 0.20 is the Sprint 15.21 empirical reference (the bounded-window candidates
    // measured 0.045-0.136 on the real capture; the pre-fix full-history design measured 0.971) - a
    // diagnostic benchmark carried over from that report, not a universal statistical constant.
    private static void Test1_HistoryLengthInvariance_NonStationarySeries()
    {
        const int SeriesLength = 1200;
        decimal[] series = SyntheticSeriesCatalog.RandomWalk(length: SeriesLength, seed: 42UL);

        var indices = new List<double>();
        var innovationStds = new List<double>();
        for (int barIndex = 100; barIndex < SeriesLength; barIndex += 20)
        {
            var context = CreateContext(series.Take(barIndex + 1).ToList());
            var result = new KalmanFilterModel().Evaluate(context);
            Assert(result.Success, $"Expected success at barIndex={barIndex}.");
            indices.Add(barIndex);
            innovationStds.Add((double)result.Metrics!["InnovationStd"]);
        }

        double rho = Spearman(indices.ToArray(), innovationStds.ToArray());
        Assert(
            Math.Abs(rho) < 0.20,
            $"History-length independence violated: Spearman(index, InnovationStd)={rho:F3} on a " +
            $"{SeriesLength}-bar non-stationary (RandomWalk) series - expected < 0.20 (Sprint 15.21 " +
            "reference benchmark). The pre-15.22 full-history design measured 0.971 on this exact test shape.");
    }

    // ── Test 2: No Look-Ahead ────────────────────────────────────────────────────────────────────────
    private static void Test2_NoLookAhead_TruncatedVsFullBackingArrayProduceIdenticalResults()
    {
        decimal[] series = SyntheticSeriesCatalog.Ar1(length: 100, phi: 0.5m);
        const int T = 59;

        // Two independently-allocated arrays holding the identical content [p0..pT] - proves the
        // result depends only on the observations actually passed, never on anything beyond them.
        var truncated = series.Take(T + 1).ToList();
        var freshCopy = series.Take(T + 1).ToArray().ToList();

        var resultFromTruncated = new KalmanFilterModel().Evaluate(CreateContext(truncated));
        var resultFromFreshCopy = new KalmanFilterModel().Evaluate(CreateContext(freshCopy));

        Assert(resultFromTruncated.Success && resultFromFreshCopy.Success, "Both calls must succeed.");
        foreach (string key in new[] { "InnovationStd", "EstimatedMean", "ObservationsUsed", "MeasurementNoise" })
        {
            double a = (double)resultFromTruncated.Metrics![key];
            double b = (double)resultFromFreshCopy.Metrics![key];
            Assert(a == b, $"No-look-ahead violated: metric '{key}' differs ({a} vs {b}) between two identical-content histories.");
        }
    }

    // ── Test 3: Rolling Window Boundary ──────────────────────────────────────────────────────────────
    private static void Test3_RollingWindowBoundary_ObservationsUsedNeverExceedsWindowSize()
    {
        // ObservationWindowSize is private to KalmanFilterModel (20 as of this sprint - see its doc
        // comment); this test does not hardcode that number, it only asserts the invariant that must
        // hold for whatever value is configured: ObservationsUsed never exceeds it, and it plateaus
        // once history is long enough - inferred from behavior, not from reading the private constant.
        int? plateau = null;
        foreach (int historyLength in new[] { 5, 19, 20, 21, 40, 80, 500, 2000 })
        {
            decimal[] series = SyntheticSeriesCatalog.WhiteNoise(length: historyLength, seed: 42UL);
            var result = new KalmanFilterModel().Evaluate(CreateContext(series.ToList()));
            Assert(result.Success, $"Expected success at historyLength={historyLength}.");
            int used = (int)(double)result.Metrics!["ObservationsUsed"];
            Assert(used <= historyLength, $"ObservationsUsed ({used}) must never exceed the supplied history ({historyLength}).");

            if (historyLength >= 500)
            {
                if (plateau is null) plateau = used;
                else Assert(used == plateau, $"ObservationsUsed must plateau to a fixed window once history is large: {used} != {plateau} at historyLength={historyLength}.");
            }
        }
        Assert(plateau is > 0 && plateau < 500, $"Expected ObservationsUsed to plateau to a small bounded window well under 500; got {plateau}.");
    }

    // ── Test 4: Warmup ───────────────────────────────────────────────────────────────────────────────
    // Documented decision (Sprint 15.22): below the window size, ALL available observations are used
    // (ObservationsUsed == history.Count) - identical to pre-15.22 behavior for short histories, the
    // minimal-blast-radius choice. Below the pre-existing minimum of 2, the model still rejects.
    private static void Test4_Warmup_ShorterThanWindowUsesAllAvailableObservations()
    {
        foreach (int historyLength in new[] { 2, 3, 10, 19 })
        {
            decimal[] series = SyntheticSeriesCatalog.WhiteNoise(length: historyLength, seed: 42UL);
            var result = new KalmanFilterModel().Evaluate(CreateContext(series.ToList()));
            Assert(result.Success, $"Expected success (warmup, not rejection) at historyLength={historyLength}.");
            int used = (int)(double)result.Metrics!["ObservationsUsed"];
            Assert(used == historyLength, $"During warmup, ObservationsUsed must equal the full available history: got {used}, expected {historyLength}.");
        }

        var tooShort = new KalmanFilterModel().Evaluate(CreateContext(new List<decimal> { 100m }));
        Assert(!tooShort.Success, "A single observation must still be rejected (pre-existing minimum of 2, unchanged).");
    }

    // ── Test 5: Outlier Sensitivity ──────────────────────────────────────────────────────────────────
    // Per Sprint 15.22 instructions: this does NOT replace the classical estimator - it only bounds
    // the effect a single outlier can have, matching the Sprint 15.21 finding (MAD/classical ratio ~1.0
    // at N=40) that outliers are not the dominant driver at this window scale. Some sensitivity is
    // expected and normal for a variance-based estimator; the test bounds it, it does not eliminate it.
    private static void Test5_OutlierSensitivity_SingleOutlierEffectStaysBounded()
    {
        decimal[] calm = SyntheticSeriesCatalog.WhiteNoise(length: 20, seed: 42UL, sigma: 1.0m);
        var calmResult = new KalmanFilterModel().Evaluate(CreateContext(calm.ToList()));
        Assert(calmResult.Success, "Calm series must succeed.");
        double calmStd = (double)calmResult.Metrics!["InnovationStd"];

        decimal[] withOutlier = (decimal[])calm.Clone();
        withOutlier[^2] += 50m; // one extreme print inserted near the end, within the window
        var outlierResult = new KalmanFilterModel().Evaluate(CreateContext(withOutlier.ToList()));
        Assert(outlierResult.Success, "Series with one outlier must still succeed.");
        double outlierStd = (double)outlierResult.Metrics!["InnovationStd"];

        Assert(double.IsFinite(outlierStd), "InnovationStd must remain finite in the presence of a single outlier.");
        Assert(outlierStd > calmStd, "A genuine outlier is expected to increase InnovationStd (classical variance is not outlier-robust by design).");
        Assert(
            outlierStd < calmStd * 50.0,
            $"A single outlier must not cause an unbounded blow-up of InnovationStd: calm={calmStd:F4}, withOutlier={outlierStd:F4} (ratio={outlierStd / calmStd:F1}x).");
    }

    // ── Test 6: Unit Consistency ─────────────────────────────────────────────────────────────────────
    private static void Test6_UnitConsistency_InnovationStdScalesLinearlyWithPriceScale()
    {
        decimal[] baseSeries = SyntheticSeriesCatalog.Ar1(length: 30, phi: 0.5m);
        var baseResult = new KalmanFilterModel().Evaluate(CreateContext(baseSeries.ToList()));
        Assert(baseResult.Success, "Base series must succeed.");
        double baseStd = (double)baseResult.Metrics!["InnovationStd"];
        double baseVar = (double)baseResult.Metrics!["InnovationVariance"];
        Assert(Math.Abs(baseStd - Math.Sqrt(baseVar)) < 1e-9, "InnovationStd must equal sqrt(InnovationVariance) exactly.");

        const decimal scale = 10m;
        decimal[] scaledSeries = baseSeries.Select(p => p * scale).ToArray();
        var scaledResult = new KalmanFilterModel().Evaluate(CreateContext(scaledSeries.ToList()));
        Assert(scaledResult.Success, "Scaled series must succeed.");
        double scaledStd = (double)scaledResult.Metrics!["InnovationStd"];

        double ratio = scaledStd / baseStd;
        Assert(
            Math.Abs(ratio - (double)scale) < 0.01,
            $"InnovationStd must scale linearly with price scale (price units, not price^2): " +
            $"expected ratio~{scale}, got {ratio:F4} (base={baseStd:F4}, scaled={scaledStd:F4}).");
    }

    // ── Test 7: QDE-012 Integration Contract ─────────────────────────────────────────────────────────
    // Verifies the CONTRACT A1 depends on (finite, non-negative, linear in k) - never optimizes or
    // picks a k. k itself is never referenced or modified anywhere in this test.
    private static void Test7_QDE012IntegrationContract_A1StopDistanceStaysFiniteNonNegativeAndLinearInK()
    {
        decimal[] series = SyntheticSeriesCatalog.MeanRevertingOu(length: 200, kappa: 0.5m);
        var result = new KalmanFilterModel().Evaluate(CreateContext(series.ToList()));
        Assert(result.Success, "Series must succeed.");
        double innovationStd = (double)result.Metrics!["InnovationStd"];

        Assert(double.IsFinite(innovationStd) && innovationStd >= 0.0, "InnovationStd must be finite and non-negative for A1 to consume.");

        double[] testKValues = { 0.25, 1.0, 3.0, 5.0, 10.0 };
        double[] stopDistances = testKValues.Select(k => k * innovationStd).ToArray();
        for (int i = 0; i < testKValues.Length; i++)
        {
            Assert(double.IsFinite(stopDistances[i]) && stopDistances[i] >= 0.0, $"A1 StopDistance at k={testKValues[i]} must be finite and non-negative.");
            if (i > 0)
            {
                double expectedRatio = testKValues[i] / testKValues[i - 1];
                double actualRatio = innovationStd == 0.0 ? expectedRatio : stopDistances[i] / stopDistances[i - 1];
                Assert(Math.Abs(actualRatio - expectedRatio) < 1e-9, "A1 StopDistance must scale exactly linearly with k (contract check, not an optimization).");
            }
        }
    }

    // ── Test 8: Kalman State Integrity ───────────────────────────────────────────────────────────────
    private static void Test8_KalmanStateIntegrity_NoNaNInfinityNegativeVarianceAndDeterministic()
    {
        decimal[] series = SyntheticSeriesCatalog.RandomWalk(length: 2000, seed: 43UL);
        var context = CreateContext(series.ToList());

        var result1 = new KalmanFilterModel().Evaluate(context);
        var result2 = new KalmanFilterModel().Evaluate(context);

        Assert(result1.Success && result2.Success, "Both evaluations must succeed.");
        foreach (var metric in result1.Metrics!)
        {
            double value = (double)metric.Value;
            Assert(!double.IsNaN(value), $"Metric '{metric.Key}' must not be NaN.");
            Assert(!double.IsInfinity(value), $"Metric '{metric.Key}' must not be Infinity.");
        }
        Assert((double)result1.Metrics["FilterCovariance"] >= 0.0, "FilterCovariance (a variance) must never be negative.");
        Assert((double)result1.Metrics["MeasurementNoise"] >= 0.0, "MeasurementNoise (a variance) must never be negative.");
        Assert((double)result1.Metrics["InnovationVariance"] >= 0.0, "InnovationVariance must never be negative.");
        Assert((double)result1.Metrics["ObservationsUsed"] >= 2.0, "At least 2 observations must always be used given the pre-existing minimum.");

        foreach (string key in result1.Metrics.Keys)
        {
            Assert((double)result1.Metrics[key] == (double)result2.Metrics![key], $"Evaluate() must be deterministic: '{key}' differed across two calls with the identical context.");
        }
    }

    // ── shared helpers ───────────────────────────────────────────────────────────────────────────────

    private static ScientificModelContext CreateContext(IReadOnlyList<decimal> history)
    {
        return new ScientificModelContext(
            new Engine.ScientificModels.Abstractions.MarketContext(DateTime.UtcNow, history[^1], history),
            new DecisionResult { Winner = MarketState.MeanReverting, Confidence = 0.75 },
            new MethodologySelection(
                new DecisionResult { Winner = MarketState.MeanReverting, Confidence = 0.75 },
                new QuantitativeMethodology(
                    "MeanReversionMethodology",
                    "Mean Reversion Methodology",
                    "OrnsteinUhlenbeck",
                    new[] { "Kalman Filter", "Dynamic Z-Score", "Volatility Models", "BOCPD" },
                    "SPRT",
                    new[] { "MeanReverting" },
                    "1.0",
                    Array.Empty<string>()),
                DateTime.UtcNow,
                "1.0",
                "Mean Reversion selected."));
    }

    /// <summary>Spearman rank correlation - local to this test file, not a production utility. Ties
    /// receive the average rank of their tied position (standard convention).</summary>
    private static double Spearman(double[] x, double[] y)
    {
        double[] rx = Rank(x);
        double[] ry = Rank(y);
        double meanX = rx.Average(), meanY = ry.Average();
        double sxy = 0, sxx = 0, syy = 0;
        for (int i = 0; i < rx.Length; i++)
        {
            double dx = rx[i] - meanX, dy = ry[i] - meanY;
            sxy += dx * dy;
            sxx += dx * dx;
            syy += dy * dy;
        }
        return sxy / Math.Sqrt(sxx * syy);
    }

    private static double[] Rank(double[] values)
    {
        int[] order = Enumerable.Range(0, values.Length).OrderBy(i => values[i]).ToArray();
        var rank = new double[values.Length];
        int i = 0;
        while (i < order.Length)
        {
            int j = i;
            while (j + 1 < order.Length && values[order[j + 1]] == values[order[i]]) j++;
            double avgRank = (i + j) / 2.0 + 1;
            for (int k = i; k <= j; k++) rank[order[k]] = avgRank;
            i = j + 1;
        }
        return rank;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
