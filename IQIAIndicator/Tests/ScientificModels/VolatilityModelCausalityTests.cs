using System;
using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Engine.ScientificModels.Context;
using IQIAIndicator.Tests.GoldenDatasets;

namespace IQIAIndicator.Tests.ScientificModels;

/// <summary>
/// Lot B2 (C-2 blocker B2 - historical causality of VolatilityModel). Pins the causal contract
/// documented on <see cref="VolatilityModel"/>:
///
///     For a history whose last element is the bar under evaluation at index T,
///     every value VolatilityModel produces is a function of X[0..T] only.
///
/// These tests were written AFTER an inspection that found VolatilityModel already satisfies this
/// property (it receives a prefix and never indexes past its last element - see the causality proof
/// in the B2 report and the doc comment on VolatilityModel itself). They therefore do not fix a
/// live look-ahead; they make the property FALSIFIABLE and lock it against future regressions -
/// notably any future introduction of a bar-index parameter, a precomputed cache, or shared state,
/// each of which would silently break causality without failing any pre-B2 test.
///
/// The window helper <see cref="ProductionHistoryWindow"/> reproduces the exact rule the live
/// pipeline uses to build MarketContext.History (IQIAIndicator.CreateScientificMarketContext:
/// a rolling window of the last 500 closes ending at the current bar), so these tests exercise the
/// real production contract rather than an idealised one.
///
/// Tolerance: every comparison is EXACT (bit-for-bit for doubles, ordinal for strings). Two
/// evaluations over identical input content traverse an identical sequence of IEEE-754 operations,
/// so any difference whatsoever proves that a value outside the prefix entered the computation -
/// which is precisely what these tests exist to detect. A tolerance would mask the defect being
/// hunted, so none is used (B2 brief section 6).
/// </summary>
public static class VolatilityModelCausalityTests
{
    /// <summary>Exact rolling-window size the live pipeline applies when building
    /// MarketContext.History - see IQIAIndicator.CreateScientificMarketContext.</summary>
    private const int ProductionHistoryWindow_Size = 500;

    public static void RunAll()
    {
        // B2-01 .. B2-05 - the causality battery, run across the golden datasets (B2 brief section 16).
        TestB2_01_PrefixProperty();
        TestB2_02_FullHistoryVsTruncatedHistory();
        TestB2_03_FuturePerturbation();
        TestB2_04_FutureAppend();
        TestB2_05_RecalculationAfterFutureCalls();

        // B2-06 .. B2-09 - edge cases and determinism (B2 brief sections 10 and 15).
        TestB2_06_InsufficientHistory();
        TestB2_07_ConstantSeries();
        TestB2_08_ExtremeVolatility();
        TestB2_09_Determinism();

        // Section 10 residual edge cases that the numbered tests above do not cover.
        TestNegativeValuesAreHandled();
        TestNaNAndInfinityAreStructurallyImpossibleInHistory();
    }

    // ── B2-01 ─────────────────────────────────────────────────────────────────────────────────────
    // Prefix property (B2 brief section 9):
    //     prefix(X, T) == prefix(Y, T)  ⇒  Volatility(X, T) == Volatility(Y, T)
    // even when X[T+1..] differs arbitrarily from Y[T+1..].
    private static void TestB2_01_PrefixProperty()
    {
        foreach ((string name, decimal[] series) in GoldenSeries(length: 300))
        {
            foreach (int bar in BarIndexes(series.Length))
            {
                // Two DIFFERENT full series that share the same prefix up to `bar`.
                decimal[] x = series.ToArray();
                decimal[] y = series.ToArray();
                for (int i = bar + 1; i < y.Length; i++)
                {
                    y[i] = y[i] * 3m + 777m;
                }

                VolatilitySnapshot fromX = Evaluate(ProductionHistoryWindow(x, bar));
                VolatilitySnapshot fromY = Evaluate(ProductionHistoryWindow(y, bar));

                AssertIdentical(
                    fromX,
                    fromY,
                    $"B2-01 prefix property violated: two series sharing the prefix X[0..{bar}] produced different results. Dataset={name}, Bar={bar}.");
            }
        }
    }

    // ── B2-02 ─────────────────────────────────────────────────────────────────────────────────────
    // Full history vs truncated history (B2 brief section 6). Under the production windowing rule,
    // evaluating bar T must give the same result whether the underlying session ends at T or extends
    // far beyond it. This is the replay invariant: a replay that stops at T and a live run that has
    // already gone past T must agree about T.
    private static void TestB2_02_FullHistoryVsTruncatedHistory()
    {
        foreach ((string name, decimal[] full) in GoldenSeries(length: 1200))
        {
            foreach (int bar in BarIndexes(full.Length))
            {
                decimal[] truncated = full.Take(bar + 1).ToArray();

                VolatilitySnapshot fromFullSession = Evaluate(ProductionHistoryWindow(full, bar));
                VolatilitySnapshot fromTruncatedSession = Evaluate(ProductionHistoryWindow(truncated, bar));

                AssertIdentical(
                    fromFullSession,
                    fromTruncatedSession,
                    $"B2-02 replay invariant violated: bar {bar} evaluated inside a {full.Length}-bar session differs from the same bar evaluated in a session truncated right after it. Dataset={name}.");
            }
        }
    }

    // ── B2-03 ─────────────────────────────────────────────────────────────────────────────────────
    // Future perturbation (B2 brief section 7): the future must be incapable of modifying the past.
    private static void TestB2_03_FuturePerturbation()
    {
        foreach ((string name, decimal[] series) in GoldenSeries(length: 300))
        {
            foreach (int bar in BarIndexes(series.Length))
            {
                VolatilitySnapshot reference = Evaluate(ProductionHistoryWindow(series, bar));

                (string label, decimal[] perturbed)[] perturbations =
                {
                    ("+1e6", PerturbAfter(series, bar, (value, _) => value + 1_000_000m)),
                    ("-1e6", PerturbAfter(series, bar, (value, _) => value - 1_000_000m)),
                    ("extreme volatility", PerturbAfter(series, bar, (value, index) => value + (index % 2 == 0 ? 500_000m : -500_000m))),
                    ("extreme trend", PerturbAfter(series, bar, (value, index) => value + 1_000m * index))
                };

                foreach ((string label, decimal[] perturbed) in perturbations)
                {
                    VolatilitySnapshot actual = Evaluate(ProductionHistoryWindow(perturbed, bar));

                    AssertIdentical(
                        reference,
                        actual,
                        $"B2-03 future perturbation changed the past: perturbation '{label}' applied strictly after bar {bar} altered the result at bar {bar}. Dataset={name}.");
                }
            }
        }
    }

    // ── B2-04 ─────────────────────────────────────────────────────────────────────────────────────
    // Future append (B2 brief section 8): compute at T, append X[T+1..N], recompute at T. Detects a
    // cache or a global reference that would retroactively rewrite the past.
    private static void TestB2_04_FutureAppend()
    {
        foreach ((string name, decimal[] series) in GoldenSeries(length: 300))
        {
            foreach (int bar in BarIndexes(series.Length))
            {
                decimal[] before = series.Take(bar + 1).ToArray();
                VolatilitySnapshot resultBefore = Evaluate(ProductionHistoryWindow(before, bar));

                var appended = new List<decimal>(before);
                appended.AddRange(series.Skip(bar + 1));
                VolatilitySnapshot resultAfter = Evaluate(ProductionHistoryWindow(appended.ToArray(), bar));

                AssertIdentical(
                    resultBefore,
                    resultAfter,
                    $"B2-04 appending future data changed the historical result at bar {bar}. Dataset={name}.");
            }
        }
    }

    // ── B2-05 ─────────────────────────────────────────────────────────────────────────────────────
    // Recalculation after future calls (B2 brief section 13): evaluate T, then evaluate T+1..T+k,
    // then re-evaluate T. Detects instance state, static state, or memoisation carried between calls.
    // Both a single shared model instance and fresh instances are exercised, because a per-instance
    // cache and a static cache fail under different call patterns.
    private static void TestB2_05_RecalculationAfterFutureCalls()
    {
        foreach ((string name, decimal[] series) in GoldenSeries(length: 300))
        {
            foreach (int bar in BarIndexes(series.Length))
            {
                var sharedModel = new VolatilityModel();

                VolatilitySnapshot first = Evaluate(ProductionHistoryWindow(series, bar), sharedModel);

                for (int future = bar + 1; future < Math.Min(bar + 25, series.Length); future++)
                {
                    Evaluate(ProductionHistoryWindow(series, future), sharedModel);
                }

                VolatilitySnapshot afterSharedInstance = Evaluate(ProductionHistoryWindow(series, bar), sharedModel);
                VolatilitySnapshot afterFreshInstance = Evaluate(ProductionHistoryWindow(series, bar));

                AssertIdentical(
                    first,
                    afterSharedInstance,
                    $"B2-05 re-evaluating bar {bar} on the SAME model instance after evaluating later bars produced a different result (instance state leak). Dataset={name}.");

                AssertIdentical(
                    first,
                    afterFreshInstance,
                    $"B2-05 re-evaluating bar {bar} on a FRESH model instance after later bars were evaluated produced a different result (static state leak). Dataset={name}.");
            }
        }
    }

    // ── B2-06 ─────────────────────────────────────────────────────────────────────────────────────
    // Insufficient history (B2 brief section 10, cases 1-5). Documents the CURRENT contract exactly;
    // this test deliberately changes nothing - see "Remaining Issues" in the B2 report for the two
    // statistical findings it pins (they are semantic, not causal, and are out of B2 scope).
    private static void TestB2_06_InsufficientHistory()
    {
        // Case 1/2/3 - below MinimumHistoryCount (3): rejected, and rejected the same way every time.
        foreach (decimal[] tooShort in new[] { Array.Empty<decimal>(), new[] { 100m }, new[] { 100m, 101m } })
        {
            var result = new VolatilityModel().Evaluate(CreateContext(tooShort));
            Assert(
                !result.Success,
                $"B2-06: a history of {tooShort.Length} observation(s) is below MinimumHistoryCount and must be rejected, never served with a fabricated reference.");
            Assert(
                result.Score == 0.0,
                "B2-06: a rejected evaluation must report Score 0.0.");
        }

        // Case 4 - exactly MinimumHistoryCount (3): accepted. Pinned here because the reference window
        // then collapses onto the current window (see the length<=21 pin below), which is exactly the
        // "insufficient history served as a valid reference" condition reported as FOLLOW-UP B2-F1.
        var atMinimum = new VolatilityModel().Evaluate(CreateContext(new[] { 100m, 101m, 100.5m }));
        Assert(atMinimum.Success, "B2-06: a history of exactly MinimumHistoryCount observations is accepted by the current contract.");

        // Structural pin (FOLLOW-UP B2-F1). ComputeReferenceVolatility uses
        // window = Min(CurrentVolatilityWindow, Count-1); for Count <= 21 that loop runs exactly once,
        // over the SAME returns ComputeCurrentVolatility uses, so ReferenceVolatility is bit-identical
        // to CurrentVolatility and RelativeVolatility is exactly 1.0 - i.e. VolatilityConfidence is
        // pinned at its MAXIMUM (1.0) for every history shorter than 22 bars. This is a statistical
        // defect, NOT a causality defect: it is deterministic and uses no future data. It is pinned
        // rather than fixed because B2 forbids changing the baseline definition (brief section 11).
        for (int length = 4; length <= 21; length++)
        {
            decimal[] series = SyntheticSeriesCatalog.WhiteNoise(length, seed: 42UL);
            VolatilitySnapshot snapshot = Evaluate(series);

            Assert(snapshot.Success, $"B2-06: WhiteNoise history of length {length} must be accepted.");
            Assert(
                snapshot.RelativeVolatility == 1.0,
                $"B2-06/FOLLOW-UP B2-F1: for a history of {length} observations (<= 21) the reference window collapses onto the current window, so RelativeVolatility is exactly 1.0. Got {snapshot.RelativeVolatility}.");
            Assert(
                snapshot.VolatilityConfidence == 1.0,
                $"B2-06/FOLLOW-UP B2-F1: RelativeVolatility == 1.0 pins VolatilityConfidence at its maximum for a history of {length} observations. Got {snapshot.VolatilityConfidence}.");
        }

        // Case 5 - full window: once the history exceeds 21 observations the reference genuinely
        // averages several distinct windows, so it is no longer degenerate.
        VolatilitySnapshot beyondWindow = Evaluate(SyntheticSeriesCatalog.WhiteNoise(120, seed: 42UL));
        Assert(beyondWindow.Success, "B2-06: a 120-observation history must be accepted.");
        Assert(
            beyondWindow.RelativeVolatility != 1.0,
            "B2-06: beyond 21 observations the reference averages several distinct windows, so RelativeVolatility is no longer pinned to 1.0.");
    }

    // ── B2-07 ─────────────────────────────────────────────────────────────────────────────────────
    // Constant series (B2 brief section 10, case 6).
    private static void TestB2_07_ConstantSeries()
    {
        foreach (int length in new[] { 3, 25, 120, 600 })
        {
            decimal[] series = SyntheticSeriesCatalog.Constant(length);
            VolatilitySnapshot snapshot = Evaluate(series);

            Assert(snapshot.Success, $"B2-07: a constant history of length {length} must still be processed.");
            Assert(snapshot.CurrentVolatility == 0.0, $"B2-07: a constant history has exactly zero current volatility (length {length}).");
            Assert(snapshot.VolatilityRegime == "LOW", $"B2-07: a constant history must classify as LOW (length {length}).");
            Assert(double.IsFinite(snapshot.RelativeVolatility), $"B2-07: RelativeVolatility must stay finite on a constant history (length {length}).");
            Assert(double.IsFinite(snapshot.VolatilityConfidence), $"B2-07: VolatilityConfidence must stay finite on a constant history (length {length}).");

            // Determinism on the degenerate path specifically.
            AssertIdentical(snapshot, Evaluate(series), $"B2-07: a constant history of length {length} must evaluate deterministically.");
        }
    }

    // ── B2-08 ─────────────────────────────────────────────────────────────────────────────────────
    // Extreme volatility, both directions (B2 brief section 10, cases 7-8).
    private static void TestB2_08_ExtremeVolatility()
    {
        var cases = new (string Name, decimal[] Series)[]
        {
            ("extremely high", AlternatingSeries(600, amplitude: 1_000_000_000m)),
            ("extremely low", SyntheticSeriesCatalog.LowVolatility(600, seed: 42UL)),
            ("high-volatility golden", SyntheticSeriesCatalog.HighVolatility(600, seed: 42UL))
        };

        foreach ((string name, decimal[] series) in cases)
        {
            VolatilitySnapshot snapshot = Evaluate(series);

            Assert(snapshot.Success, $"B2-08: the '{name}' series must be processed without failure.");
            Assert(double.IsFinite(snapshot.CurrentVolatility), $"B2-08: CurrentVolatility must stay finite on the '{name}' series.");
            Assert(double.IsFinite(snapshot.ReferenceVolatility), $"B2-08: ReferenceVolatility must stay finite on the '{name}' series.");
            Assert(double.IsFinite(snapshot.RelativeVolatility), $"B2-08: RelativeVolatility must stay finite on the '{name}' series.");
            Assert(
                snapshot.VolatilityConfidence >= 0.0 && snapshot.VolatilityConfidence <= 1.0,
                $"B2-08: VolatilityConfidence must stay within [0,1] on the '{name}' series. Got {snapshot.VolatilityConfidence}.");
            Assert(
                snapshot.VolatilityPercentile >= 0.0 && snapshot.VolatilityPercentile <= 1.0,
                $"B2-08: VolatilityPercentile must stay within [0,1] on the '{name}' series. Got {snapshot.VolatilityPercentile}.");

            // Causality must hold on the extreme paths too, not only on the well-behaved ones.
            int bar = series.Length - 1;
            decimal[] perturbed = PerturbAfter(series, bar - 50, (value, _) => value * 7m + 1_000m);
            AssertIdentical(
                Evaluate(ProductionHistoryWindow(series, bar - 50)),
                Evaluate(ProductionHistoryWindow(perturbed, bar - 50)),
                $"B2-08: future perturbation altered the past on the '{name}' series.");
        }
    }

    // ── B2-09 ─────────────────────────────────────────────────────────────────────────────────────
    // Determinism (B2 brief section 15): no clock, no randomness, no global state, no dependence on
    // call order.
    private static void TestB2_09_Determinism()
    {
        foreach ((string name, decimal[] series) in GoldenSeries(length: 300))
        {
            decimal[] history = ProductionHistoryWindow(series, series.Length - 1);

            VolatilitySnapshot first = Evaluate(history);
            for (int repeat = 0; repeat < 5; repeat++)
            {
                AssertIdentical(
                    first,
                    Evaluate(history),
                    $"B2-09: repeated evaluation of identical input produced a different result. Dataset={name}, repeat={repeat}.");
            }

            // A separately-allocated but value-identical history must give the identical result: this
            // rules out any dependence on the identity (rather than the content) of the input list.
            AssertIdentical(
                first,
                Evaluate(history.ToArray()),
                $"B2-09: a value-identical but separately allocated history produced a different result. Dataset={name}.");
        }
    }

    // ── Section 10, case 10 ───────────────────────────────────────────────────────────────────────
    private static void TestNegativeValuesAreHandled()
    {
        decimal[] positive = SyntheticSeriesCatalog.WhiteNoise(120, seed: 42UL);
        var negative = new decimal[positive.Length];
        for (int i = 0; i < positive.Length; i++)
        {
            negative[i] = positive[i] - 200m;
        }

        VolatilitySnapshot snapshot = Evaluate(negative);

        Assert(snapshot.Success, "Section 10 case 10: a history containing negative values must be processed without failure.");
        Assert(double.IsFinite(snapshot.CurrentVolatility), "Section 10 case 10: CurrentVolatility must stay finite on a negative-valued history.");
        Assert(double.IsFinite(snapshot.RelativeVolatility), "Section 10 case 10: RelativeVolatility must stay finite on a negative-valued history.");

        // VolatilityModel reads only first differences, so a constant level shift must not change any
        // volatility output at all.
        AssertIdentical(
            Evaluate(positive),
            snapshot,
            "Section 10 case 10: VolatilityModel consumes first differences only, so a constant level shift must leave every volatility metric unchanged.");
    }

    /// <summary>
    /// Section 10 case 9. MarketContext.History is IReadOnlyList&lt;decimal&gt;, and System.Decimal has
    /// no NaN or Infinity representation, so a non-finite value cannot enter VolatilityModel through
    /// the history at all - the case is structurally impossible rather than merely unhandled. The
    /// non-finite path that DOES exist is the prior-metrics one (doubles), already covered by
    /// VolatilityModelTests.TestNaNAndInfinityAreHandled. This test states the structural guarantee so
    /// the absence of a history-side NaN test is a recorded decision rather than an omission.
    /// </summary>
    private static void TestNaNAndInfinityAreStructurallyImpossibleInHistory()
    {
        Assert(
            typeof(MarketContext).GetProperty(nameof(MarketContext.History))!.PropertyType == typeof(IReadOnlyList<decimal>),
            "Section 10 case 9: MarketContext.History must remain IReadOnlyList<decimal>; if it ever becomes a floating-point type, a history-side NaN/Infinity test becomes required.");

        // The largest magnitudes decimal can carry still produce finite doubles through the model.
        VolatilitySnapshot snapshot = Evaluate(AlternatingSeries(120, amplitude: 1_000_000_000_000m));
        Assert(snapshot.Success, "Section 10 case 9: very large (but representable) decimal magnitudes must not fail the model.");
        Assert(double.IsFinite(snapshot.CurrentVolatility), "Section 10 case 9: very large decimal magnitudes must still produce a finite CurrentVolatility.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reproduces exactly the rule the live pipeline uses to build MarketContext.History
    /// (IQIAIndicator.CreateScientificMarketContext): a rolling window of the last
    /// <see cref="ProductionHistoryWindow_Size"/> closes, ENDING at the bar under evaluation. The
    /// window never extends past <paramref name="bar"/> - that is the caller-side half of the causal
    /// contract, and reproducing it here is what makes these tests exercise the production path.
    /// </summary>
    private static decimal[] ProductionHistoryWindow(IReadOnlyList<decimal> series, int bar)
    {
        int endIndex = Math.Min(bar, series.Count - 1);
        int startIndex = Math.Max(0, endIndex - ProductionHistoryWindow_Size + 1);
        var window = new decimal[endIndex - startIndex + 1];
        for (int i = 0; i < window.Length; i++)
        {
            window[i] = series[startIndex + i];
        }

        return window;
    }

    private static decimal[] PerturbAfter(decimal[] series, int bar, Func<decimal, int, decimal> perturbation)
    {
        decimal[] perturbed = series.ToArray();
        for (int i = bar + 1; i < perturbed.Length; i++)
        {
            perturbed[i] = perturbation(perturbed[i], i);
        }

        return perturbed;
    }

    private static decimal[] AlternatingSeries(int length, decimal amplitude)
    {
        var series = new decimal[length];
        for (int i = 0; i < length; i++)
        {
            series[i] = 100m + (i % 2 == 0 ? amplitude : -amplitude);
        }

        return series;
    }

    /// <summary>The golden datasets named by B2 brief section 16, plus Constant as the degenerate
    /// reference case. Used only to exercise causality/determinism - never to calibrate anything.</summary>
    private static (string Name, decimal[] Series)[] GoldenSeries(int length) => new[]
    {
        ("WhiteNoise", SyntheticSeriesCatalog.WhiteNoise(length, seed: 42UL)),
        ("RandomWalk", SyntheticSeriesCatalog.RandomWalk(length, seed: 42UL)),
        ("MeanRevertingOu", SyntheticSeriesCatalog.MeanRevertingOu(length, seed: 42UL)),
        ("Ar1", SyntheticSeriesCatalog.Ar1(length, seed: 42UL)),
        ("VarianceBreak", SyntheticSeriesCatalog.VarianceBreak(length, seed: 42UL)),
        ("Trending", SyntheticSeriesCatalog.Trending(length, seed: 42UL)),
        ("HighVolatility", SyntheticSeriesCatalog.HighVolatility(length, seed: 42UL)),
        ("LowVolatility", SyntheticSeriesCatalog.LowVolatility(length, seed: 42UL))
    };

    /// <summary>Bars sampled for the causality battery: a warmup bar, bars either side of the
    /// mid-series break the VarianceBreak/StructuralBreak datasets carry (the case most likely to
    /// expose a leak), a bar past the 500-observation production window cap, and the last bar.</summary>
    private static int[] BarIndexes(int seriesLength)
    {
        int[] candidates = { 25, 60, 149, 150, 151, 299, 520, 700, 1100 };
        var bars = new List<int>();
        foreach (int candidate in candidates)
        {
            if (candidate < seriesLength)
            {
                bars.Add(candidate);
            }
        }

        if (seriesLength > 0)
        {
            bars.Add(seriesLength - 1);
        }

        return bars.Distinct().ToArray();
    }

    private static VolatilitySnapshot Evaluate(IReadOnlyList<decimal> history, VolatilityModel? model = null)
    {
        var result = (model ?? new VolatilityModel()).Evaluate(CreateContext(history));
        return VolatilitySnapshot.From(result);
    }

    /// <summary>The exact, comparable output surface of VolatilityModel. Diagnostics is excluded
    /// deliberately: it is a dictionary whose entries are copies of the (fixture-constant) prior
    /// metrics plus the values already compared here, so including it would add no discriminating
    /// power.</summary>
    private readonly record struct VolatilitySnapshot(
        bool Success,
        double Score,
        double CurrentVolatility,
        double ReferenceVolatility,
        double RelativeVolatility,
        double VolatilityPercentile,
        string VolatilityRegime,
        double VolatilityConfidence)
    {
        public static VolatilitySnapshot From(ScientificModelResult result)
        {
            if (!result.Success || result.Metrics is null)
            {
                return new VolatilitySnapshot(false, result.Score, 0.0, 0.0, 0.0, 0.0, string.Empty, 0.0);
            }

            return new VolatilitySnapshot(
                true,
                result.Score,
                Number(result.Metrics, "CurrentVolatility"),
                Number(result.Metrics, "ReferenceVolatility"),
                Number(result.Metrics, "RelativeVolatility"),
                Number(result.Metrics, "VolatilityPercentile"),
                Text(result.Metrics, "VolatilityRegime"),
                Number(result.Metrics, "VolatilityConfidence"));
        }

        private static double Number(IReadOnlyDictionary<string, object> metrics, string key) =>
            metrics.TryGetValue(key, out object? raw) && raw is double value
                ? value
                : throw new InvalidOperationException($"Metric '{key}' is missing or not a double.");

        private static string Text(IReadOnlyDictionary<string, object> metrics, string key) =>
            metrics.TryGetValue(key, out object? raw) && raw is string value
                ? value
                : throw new InvalidOperationException($"Metric '{key}' is missing or not a string.");
    }

    /// <summary>Bit-exact comparison - see the tolerance rationale in this class's doc comment.</summary>
    private static void AssertIdentical(VolatilitySnapshot expected, VolatilitySnapshot actual, string message)
    {
        Assert(expected.Success == actual.Success, $"{message} (Success {expected.Success} vs {actual.Success})");
        Assert(expected.Score.Equals(actual.Score), $"{message} (Score {expected.Score:R} vs {actual.Score:R})");
        Assert(expected.CurrentVolatility.Equals(actual.CurrentVolatility), $"{message} (CurrentVolatility {expected.CurrentVolatility:R} vs {actual.CurrentVolatility:R})");
        Assert(expected.ReferenceVolatility.Equals(actual.ReferenceVolatility), $"{message} (ReferenceVolatility {expected.ReferenceVolatility:R} vs {actual.ReferenceVolatility:R})");
        Assert(expected.RelativeVolatility.Equals(actual.RelativeVolatility), $"{message} (RelativeVolatility {expected.RelativeVolatility:R} vs {actual.RelativeVolatility:R})");
        Assert(expected.VolatilityPercentile.Equals(actual.VolatilityPercentile), $"{message} (VolatilityPercentile {expected.VolatilityPercentile:R} vs {actual.VolatilityPercentile:R})");
        Assert(string.Equals(expected.VolatilityRegime, actual.VolatilityRegime, StringComparison.Ordinal), $"{message} (VolatilityRegime '{expected.VolatilityRegime}' vs '{actual.VolatilityRegime}')");
        Assert(expected.VolatilityConfidence.Equals(actual.VolatilityConfidence), $"{message} (VolatilityConfidence {expected.VolatilityConfidence:R} vs {actual.VolatilityConfidence:R})");
    }

    /// <summary>Mirrors VolatilityModelTests.CreateContext: the prior Kalman/OU/DynamicZScore results
    /// are a FIXED fixture, identical for every evaluation in this suite, so any difference observed
    /// between two evaluations is attributable to the history alone.</summary>
    private static ScientificModelContext CreateContext(IReadOnlyList<decimal> history)
    {
        var marketContext = new MarketContext(
            new DateTime(2026, 01, 01, 0, 0, 0, DateTimeKind.Utc),
            100m,
            history);

        var decisionResult = new DecisionResult { Winner = MarketState.MeanReverting, Confidence = 1.0 };
        var methodologySelection = new MethodologySelection(
            decisionResult,
            new QuantitativeMethodology(
                "MeanReversionMethodology",
                "Mean Reversion Methodology",
                "VolatilityModel",
                new[] { "KalmanFilterModel", "OrnsteinUhlenbeckModel", "DynamicZScoreModel" },
                "SPRT",
                new[] { "MeanReverting" },
                "1.0",
                Array.Empty<string>()),
            new DateTime(2026, 01, 01, 0, 0, 0, DateTimeKind.Utc),
            "1.0",
            "Mean Reversion selected.");

        return new ScientificModelContext(
            marketContext,
            decisionResult,
            methodologySelection,
            CreatePriorResults());
    }

    private static IReadOnlyList<ScientificModelResult> CreatePriorResults()
    {
        var metrics = new Dictionary<string, object>
        {
            [ScientificMetricKeys.EstimatedMean] = 100.0,
            [ScientificMetricKeys.InnovationStd] = 2.0,
            [ScientificMetricKeys.KalmanGain] = 0.8,
            [ScientificMetricKeys.EstimatedTheta] = 0.2,
            [ScientificMetricKeys.HalfLife] = 5.0,
            [ScientificMetricKeys.MeanReversionStrength] = 0.9,
            [ScientificMetricKeys.DynamicZScore] = 0.5,
            [ScientificMetricKeys.NormalizedDistance] = 0.5
        };

        return new[]
        {
            new ScientificModelResult("KalmanFilterModel", true, 1.0, "Synthetic Kalman result.", metrics),
            new ScientificModelResult("OrnsteinUhlenbeckModel", true, 1.0, "Synthetic OU result.", metrics),
            new ScientificModelResult("DynamicZScoreModel", true, 1.0, "Synthetic Dynamic Z-Score result.", metrics)
        };
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
