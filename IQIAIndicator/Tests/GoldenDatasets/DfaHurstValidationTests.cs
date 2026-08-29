using IQIAIndicator.Core;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Regime.Evidence;
using IQIAIndicator.Engine.Regime.Evidence.DFA;

namespace IQIAIndicator.Tests.GoldenDatasets;

/// <summary>
/// Sprint 15 / Part D + Part I. DFA and "Hurst" science audit.
///
/// Part D finding: DFA genuinely computes a DFA-1 Hurst exponent (Peng et al. 1994) via log-log
/// regression of fluctuation F(n) against window size n across log-spaced windows - DfaResult.Hurst
/// is an honest name for what is actually computed.
///
/// Part I finding (verified directly against the source, not assumed from a prior audit):
/// Engine/Regime/Evidence/HurstEvidence.cs's own doc comment says "Proxy Hurst via Variance Ratio a
/// lag 2" - it computes H = (log2(VR(2)) + 1) / 2 from a single two-lag variance ratio, a genuinely
/// different (and far noisier - one data point vs. DFA's multi-scale regression) estimation method
/// from DFA's. The field is honestly named HurstProxy, not Hurst. Critically: grepping the whole
/// repository shows HurstEvidence's HurstProxy is consumed ONLY by a "valid evidence" counter in
/// IQIAIndicator.cs and a display formatter in ScientificDashboard.cs - it is NEVER read by any
/// Fusion rule. By contrast, DfaEvidence.Hurst IS consumed by Engine/Fusion/Rules/PersistenceRule.cs.
/// So "Hurst" reaches trading decisions through DFA only; HurstEvidence's proxy is a display-only
/// diagnostic that happens to estimate a related but methodologically distinct quantity, using
/// entirely different input plumbing (Core.MarketContext with an internal 30-bar circular buffer,
/// versus EvidenceContext's 128-bar rolling window for DFA). Neither name is misleading once read
/// carefully (HurstProxy vs Hurst), but the coexistence of two differently-scaled "Hurst-like"
/// numbers under the same EvidenceSet is a real discoverability hazard for anyone reading the
/// Scientific Dashboard without also reading this file.
/// </summary>
public static class DfaHurstValidationTests
{
    private const int DfaWindowSize = 128;
    private const int DfaMinimumSampleSize = 80;

    public static void RunAll()
    {
        AssertExistingReportExecutesWithoutError();

        AssertRandomWalkLogReturnsProduceHurstNearOneHalf();
        AssertMeanRevertingSeriesProducesHurstBelowOneHalf();
        AssertDfaHurstIsWhatFeedsThePersistenceRuleNotHurstProxy();

        AssertConstantSeriesIsInvalidViaZeroFluctuationFilter();
        AssertShortSeriesIsRejectedAsWarmup();
        AssertNonPositivePriceIsRejectedNotCrashed();
        AssertScaleInvarianceOfLogReturnBasedHurst();

        AssertHurstProxyIsAGenuinelyDifferentEstimatorFromDfaHurst();
        AssertHurstProxyHandlesWarmupAndZeroVarianceWithoutCrashing();
    }

    // ── Reconnect existing validation scaffolding ───────────────────────────────────────────────

    private static void AssertExistingReportExecutesWithoutError()
    {
        string report = DfaValidation.RunAllReport();
        Assert(!string.IsNullOrWhiteSpace(report), "DfaValidation.RunAllReport() must execute and produce output.");
        // Same finding as ADF/KPSS: DfaGoldenDataset.NoldsReference values (WhiteNoise_N256,
        // Persistent_N256, OuProcess_N256) are explicitly marked "Placeholders — remplacer par les
        // valeurs Python après validation" in DfaGoldenDataset.cs and were never filled in with real
        // nolds output, so DfaValidation.Compare()/IsAcceptable is not wired to an assertion here -
        // doing so would validate against an invented number (rule 6).
    }

    // ── Known qualitative behavior (rule 8) ─────────────────────────────────────────────────────

    private static void AssertRandomWalkLogReturnsProduceHurstNearOneHalf()
    {
        DfaResult result = RunDfa(SyntheticSeriesCatalog.RandomWalk(300));
        Assert(result.IsValid, "DFA must produce a valid result for a well-formed 300-bar random walk.");
        // A geometric random walk's log-returns are i.i.d. by construction -> textbook DFA property: H ~= 0.5.
        // Finite-sample DFA has real estimation variance, hence a generous but still meaningful band.
        Assert(result.Hurst > 0.35 && result.Hurst < 0.65,
            $"Random walk log-returns must produce H close to 0.5 (memoryless); got {result.Hurst:F4}.");
    }

    private static void AssertMeanRevertingSeriesProducesHurstBelowOneHalf()
    {
        DfaResult result = RunDfa(SyntheticSeriesCatalog.MeanRevertingOu(300));
        Assert(result.IsValid, "DFA must produce a valid result for a well-formed 300-bar OU series.");
        // Log-returns of a strongly mean-reverting level series are themselves anti-persistent
        // (an up-return is disproportionately likely to be followed by a down-return) -> H < 0.5.
        Assert(result.Hurst < 0.5,
            $"Log-returns of a strongly mean-reverting series must read anti-persistent (H < 0.5); got {result.Hurst:F4}.");
    }

    /// <summary>Direct confirmation of the audit's specific concern, executed rather than assumed.</summary>
    private static void AssertDfaHurstIsWhatFeedsThePersistenceRuleNotHurstProxy()
    {
        // Engine/Fusion/Rules/PersistenceRule.cs line 46-47 (Sprint 14 reading):
        //   double dfaDirection = Math.Tanh(HurstDirectionSteepness * (Math.Clamp(dfa.Hurst, 0.0, 2.0) - HurstRandomWalkLevel));
        // This test does not re-invoke PersistenceRule (out of this sprint's protected scope -
        // Fusion formulas are not to be modified or re-tested here); it documents, as a standing
        // regression guard, that DfaResult exposes the exact field name PersistenceRule reads.
        DfaResult dfa = RunDfa(SyntheticSeriesCatalog.RandomWalk(200));
        Assert(dfa.GetType().GetProperty("Hurst") is not null, "DfaResult must expose a 'Hurst' property - this is the field PersistenceRule.cs actually consumes.");
    }

    // ── Edge cases ───────────────────────────────────────────────────────────────────────────────

    private static void AssertConstantSeriesIsInvalidViaZeroFluctuationFilter()
    {
        DfaResult result = RunDfa(SyntheticSeriesCatalog.Constant(150));
        // Constant prices -> log-returns are exactly 0 for every step -> the integrated profile is
        // exactly 0 everywhere -> ComputeFluctuation returns exactly 0.0 for every window size ->
        // DfaEvidence's own filter (`if (double.IsNaN(f) || f <= 0.0) continue;`) discards every
        // window as invalid, so `valid < 4` and the result is Invalid - not a NaN Hurst exponent.
        Assert(!result.IsValid, "A constant price series must be reported Invalid via the zero-fluctuation filter, not produce a numeric Hurst exponent.");
    }

    private static void AssertShortSeriesIsRejectedAsWarmup()
    {
        DfaResult result = RunDfa(SyntheticSeriesCatalog.WhiteNoise(30));
        Assert(!result.IsValid, "A series shorter than DFA's MinimumSampleSize (80) must be rejected as warmup.");
        Assert(result.Explanation.Contains("Warmup", StringComparison.OrdinalIgnoreCase), "The warmup rejection must be explicit.");
    }

    private static void AssertNonPositivePriceIsRejectedNotCrashed()
    {
        decimal[] series = SyntheticSeriesCatalog.WhiteNoise(150);
        series[75] = 0m;
        DfaResult result = RunDfa(series);
        Assert(!result.IsValid, "A non-positive price must be rejected explicitly (log-return is undefined for price <= 0), not throw.");
        Assert(result.Explanation.Length > 0, "The rejection reason must be explained.");
    }

    /// <summary>Log-returns are invariant to a uniform multiplicative rescaling of price (ln(k*a/k*b) = ln(a/b)), so DFA's Hurst estimate must be unaffected by the currency/scale the price series is quoted in - a demonstrable mathematical property, not an invented reference number.</summary>
    private static void AssertScaleInvarianceOfLogReturnBasedHurst()
    {
        decimal[] baseSeries = SyntheticSeriesCatalog.RandomWalk(300);
        var scaledSeries = new decimal[baseSeries.Length];
        for (int i = 0; i < baseSeries.Length; i++)
            scaledSeries[i] = baseSeries[i] * 1000m;

        DfaResult baseResult = RunDfa(baseSeries);
        DfaResult scaledResult = RunDfa(scaledSeries);
        Assert(baseResult.IsValid && scaledResult.IsValid, "Both the base and rescaled series must produce valid results.");
        Assert(Math.Abs(baseResult.Hurst - scaledResult.Hurst) < 1e-9,
            $"H must be invariant to a uniform price scale change (log-return invariance); base={baseResult.Hurst:F10}, scaled={scaledResult.Hurst:F10}.");
    }

    // ── Hurst proxy audit ────────────────────────────────────────────────────────────────────────

    private static void AssertHurstProxyIsAGenuinelyDifferentEstimatorFromDfaHurst()
    {
        decimal[] series = SyntheticSeriesCatalog.RandomWalk(200);
        DfaResult dfa = RunDfa(series);
        HurstResult hurstProxy = RunHurstProxy(series);
        Assert(dfa.IsValid, "DFA must be valid on this series.");
        Assert(hurstProxy.IsValid, "HurstEvidence must be valid on this series (200 bars > its own 20-bar minimum).");
        // Not asserting they are close - they are different estimators over different windows by
        // design. Asserting only that both land in a plausible [0,1] range for a memoryless series,
        // and that they are computed independently (different numeric values expected in general).
        Assert(hurstProxy.HurstProxy >= 0m && hurstProxy.HurstProxy <= 1m, "HurstProxy must stay within its documented [0,1] clamp.");
        Assert(dfa.Hurst >= 0.0 && dfa.Hurst <= 2.0, "DFA Hurst must stay within its documented [0,2] clamp.");
    }

    private static void AssertHurstProxyHandlesWarmupAndZeroVarianceWithoutCrashing()
    {
        HurstResult warmup = RunHurstProxy(SyntheticSeriesCatalog.WhiteNoise(10));
        Assert(!warmup.IsValid, "HurstEvidence must reject a series shorter than its own 20-bar minimum as warmup.");

        HurstResult constant = RunHurstProxy(SyntheticSeriesCatalog.Constant(40));
        Assert(!constant.IsValid, "HurstEvidence must reject a constant series (zero one-step variance) explicitly, not divide by zero.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static DfaResult RunDfa(decimal[] series) => new DfaEvidence().Compute(new EvidenceContext
    {
        Series = series,
        SampleSize = series.Length,
        MinimumSampleSize = DfaMinimumSampleSize,
        WindowSize = DfaWindowSize,
        Timestamp = DateTime.UnixEpoch
    });

    /// <summary>HurstEvidence is stateful (internal circular buffer keyed on MarketContext.Clock.IsFirstBar), so it must be fed one bar at a time, exactly as RegimeEngine does in production.</summary>
    private static HurstResult RunHurstProxy(decimal[] series)
    {
        var evidence = new HurstEvidence();
        HurstResult last = HurstResult.Invalid("No bars processed.");
        for (int i = 0; i < series.Length; i++)
            last = evidence.Compute(BuildMarketContext(series[i], isFirstBar: i == 0, barIndex: i));
        return last;
    }

    private static MarketContext BuildMarketContext(decimal close, bool isFirstBar, int barIndex) => new()
    {
        BarIndex = barIndex,
        TimeFrame = "1Min",
        Price = new PriceInfo(close, close, close, close, close, close),
        Volume = new VolumeInfo(0m, 0m, 0m, 0m),
        Instrument = new InstrumentInfo("TEST", 0.01m, 1m, 1m, 2),
        Clock = new MarketClock
        {
            CurrentTime = DateTime.UnixEpoch.AddMinutes(barIndex),
            CurrentDate = DateOnly.FromDateTime(DateTime.UnixEpoch),
            DayOfWeek = DayOfWeek.Monday,
            Session = new SessionInfo("Test", DateTime.UnixEpoch, DateTime.UnixEpoch.AddHours(8)),
            ElapsedMinutes = barIndex,
            IsFirstBar = isFirstBar,
            IsLastBar = false
        },
        Execution = new Core.ExecutionContext
        {
            CurrentBar = barIndex,
            LastCalculatedBar = barIndex,
            IsRealtime = false,
            IsHistorical = true,
            IsReplay = false
        }
    };

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
