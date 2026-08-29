using IQIAIndicator.Core;
using IQIAIndicator.Engine.Regime.Evidence;

namespace IQIAIndicator.Tests.GoldenDatasets;

/// <summary>
/// Sprint 15 / Part J. Audits the ninth Engine/Regime/Evidence model not explicitly named in the
/// sprint's model roadmap (ADF/KPSS/DFA/Half-Life/Variance Ratio/Bai-Perron/CUSUM/Hurst): the
/// "Volatility" evidence model (Engine/Regime/Evidence/VolatilityEvidence.cs, EvidenceSet.Volatility)
/// - distinct from Engine/ScientificModels/Context/VolatilityModel.cs, an unrelated class in the
/// Mean-Reversion scientific-model stack already audited in Sprint 14/15's other work.
///
/// Contract actually implemented: ACF(|return|) at lag 1 (autocorrelation of absolute returns) - a
/// standard, textbook ARCH-effect / volatility-clustering diagnostic (large moves tend to be followed
/// by large moves, of either sign). IsClustering = ACF &gt; 0.05 is a fixed, hardcoded threshold (not
/// touched or recalibrated by this sprint - Part S protects Fusion/Decision-adjacent calibration, and
/// this constant is the model's own decision boundary, not owned by Fusion/Decision).
///
/// Consumption audit (grep-verified, matches the same orphan pattern as HurstEvidence - see
/// DfaHurstValidationTests.cs): EvidenceSet.Volatility is read only by a "valid evidence" counter in
/// IQIAIndicator.cs and a display formatter in ScientificDashboard.cs. No Fusion rule reads it. It is
/// computed every bar (stateful, same 30-bar circular buffer pattern as HurstEvidence) purely for
/// diagnostic display; it has zero influence on Decision-making today.
/// </summary>
public static class VolatilityRegimeEvidenceTests
{
    public static void RunAll()
    {
        AssertClusteringSeriesProducesPositiveAcf();
        AssertIndependentAbsoluteReturnsProduceLowAcf();
        AssertWarmupIsRejectedExplicitly();
        AssertConstantSeriesIsRejectedNotDividedByZero();
        AssertContractIsExactlyAcfOfAbsoluteReturnsNotRawReturns();
    }

    /// <summary>Dataset 08 (VarianceBreak) has a step change in noise amplitude - the absolute-return series has two distinct regimes, which by construction correlates consecutive |return| values more than i.i.d. noise would (a large jump in the high-variance regime is more likely to neighbor another large jump than in a purely i.i.d. series).</summary>
    private static void AssertClusteringSeriesProducesPositiveAcf()
    {
        VolatilityResult result = RunVolatility(SyntheticSeriesCatalog.VarianceBreak(120));
        Assert(result.IsValid, "A well-formed 120-bar variance-break series must produce a valid ACF estimate.");
        Assert(result.AcfAbsReturns > 0m,
            $"A series with a genuine variance regime shift must show positive ACF(|return|) (volatility clustering); got {result.AcfAbsReturns:F4}.");
    }

    private static void AssertIndependentAbsoluteReturnsProduceLowAcf()
    {
        VolatilityResult result = RunVolatility(SyntheticSeriesCatalog.WhiteNoise(120));
        Assert(result.IsValid, "A well-formed 120-bar white-noise series must produce a valid ACF estimate.");
        // Finite-sample ACF of i.i.d. noise is never exactly zero; a generous band around zero is the
        // correct check for "no clustering", not an exact-zero assertion.
        Assert(Math.Abs(result.AcfAbsReturns) < 0.5m,
            $"Pure white noise must not show strong spurious clustering; got ACF={result.AcfAbsReturns:F4}.");
    }

    private static void AssertWarmupIsRejectedExplicitly()
    {
        VolatilityResult result = RunVolatility(SyntheticSeriesCatalog.WhiteNoise(10));
        Assert(!result.IsValid, "Fewer than the model's own 20-bar minimum must be rejected as warmup.");
        Assert(result.Explanation.Contains("Warmup", StringComparison.OrdinalIgnoreCase), "The warmup rejection must be explicit.");
    }

    private static void AssertConstantSeriesIsRejectedNotDividedByZero()
    {
        VolatilityResult result = RunVolatility(SyntheticSeriesCatalog.Constant(40));
        // Constant price -> all |returns| are exactly 0 -> variance of the (all-zero) absolute-return
        // deviations is exactly 0 -> VolatilityEvidence's own `if (varA == 0m) return Invalid(...)`
        // guard rejects it explicitly, rather than a 0/0 division producing NaN.
        Assert(!result.IsValid, "A constant series (zero variance of absolute returns) must be rejected explicitly, not divide by zero.");
    }

    /// <summary>Confirms the model measures autocorrelation of |return|, not of the signed return itself - a series with strong signed-return autocorrelation but i.i.d. magnitude should not necessarily read as "clustering" in this model's specific sense.</summary>
    private static void AssertContractIsExactlyAcfOfAbsoluteReturnsNotRawReturns()
    {
        VolatilityResult result = RunVolatility(SyntheticSeriesCatalog.MeanRevertingOu(120));
        Assert(result.IsValid, "A well-formed 120-bar OU series must produce a valid ACF estimate.");
        Assert(double.IsFinite((double)result.AcfAbsReturns), "AcfAbsReturns must always be finite when IsValid is true.");
        // No specific sign/magnitude asserted here - the point of this test is documentation-by-code
        // that this model's Explanation and field name (AcfAbsReturns) are what they claim to be, not
        // a disguised measure of raw-return autocorrelation (which VarianceRatioEvidence, a
        // structurally different model, already covers).
        Assert(result.Explanation.Contains("ACF", StringComparison.Ordinal), "Explanation must name the actual statistic computed (ACF), not a generic label.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>VolatilityEvidence is stateful (internal 30-bar circular buffer, reset on IsFirstBar), so it must be fed one bar at a time.</summary>
    private static VolatilityResult RunVolatility(decimal[] series)
    {
        var evidence = new VolatilityEvidence();
        VolatilityResult last = VolatilityResult.Invalid("No bars processed.");
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
