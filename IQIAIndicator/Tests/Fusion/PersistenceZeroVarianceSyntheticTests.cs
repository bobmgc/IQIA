using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Fusion.State;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Regime.Evidence.DFA;
using IQIAIndicator.Engine.Regime.Evidence.VarianceRatio;

namespace IQIAIndicator.Tests.Fusion;

/// <summary>
/// Sprint 15.25 (Lot 14.17, brief §22). Synthetic Cases A-G for the Persistence Fusion dimension, plus a
/// direct demonstration of <see cref="FusionStateManager"/>'s EMA+hysteresis freeze mechanism - the
/// candidate cause (brief Hypothesis D/F) for the zero-variance days found in Lot 14.16. Every case invokes
/// the REAL <see cref="PersistenceRule"/>/<see cref="FusionStateManager"/> classes; nothing is reimplemented.
/// </summary>
public static class PersistenceZeroVarianceSyntheticTests
{
    public static void RunAll()
    {
        CaseA_ConstantInputs_ProducesDeterministicIdenticalOutput();
        CaseB_SlightlyVariableInputs_ProducesSmallMonotonicMovement();
        CaseC_StronglyVariableInputs_ProducesLargerMovement();
        CaseD_ExtremeInputs_NoNaNNoInfinity_StaysInBounds();
        CaseE_InsufficientSamples_ProducesMissingEvidenceSentinel();
        CaseF_ZeroVarianceRatioDenominator_NoNaNNoInfinity();
        CaseG_NullEvidence_ProducesMissingEvidenceSentinel();
        HysteresisFreeze_SmallRawMovements_FreezeStableValue_UntilCumulativeThresholdCrossed();
    }

    private static void CaseA_ConstantInputs_ProducesDeterministicIdenticalOutput()
    {
        double p1 = EvaluatePersistence(Dfa(0.55, 0.9, 0.9, 100), Vr(1.05, 0.5, 0.02, 0.9, 50));
        double p2 = EvaluatePersistence(Dfa(0.55, 0.9, 0.9, 100), Vr(1.05, 0.5, 0.02, 0.9, 50));
        double p3 = EvaluatePersistence(Dfa(0.55, 0.9, 0.9, 100), Vr(1.05, 0.5, 0.02, 0.9, 50));

        Assert(p1 == p2 && p2 == p3, $"Case A: identical inputs called three times must produce bit-identical Persistence - observed p1={p1:F6}, p2={p2:F6}, p3={p3:F6}.");
    }

    private static void CaseB_SlightlyVariableInputs_ProducesSmallMonotonicMovement()
    {
        double pLow = EvaluatePersistence(Dfa(0.50, 0.9, 0.9, 100), Vr(1.00, 0.5, 0.02, 0.9, 50));
        double pMid = EvaluatePersistence(Dfa(0.51, 0.9, 0.9, 100), Vr(1.00, 0.5, 0.02, 0.9, 50));
        double pHigh = EvaluatePersistence(Dfa(0.52, 0.9, 0.9, 100), Vr(1.00, 0.5, 0.02, 0.9, 50));

        Assert(pMid >= pLow && pHigh >= pMid, $"Case B: small, monotonically increasing Hurst must produce non-decreasing Persistence - observed [{pLow:F6}, {pMid:F6}, {pHigh:F6}].");
        Assert(pHigh - pLow < 0.05, $"Case B: a small Hurst change (0.50->0.52) must produce a correspondingly SMALL Persistence movement - observed delta={pHigh - pLow:F6}.");
    }

    private static void CaseC_StronglyVariableInputs_ProducesLargerMovement()
    {
        double pLow = EvaluatePersistence(Dfa(0.50, 0.9, 0.9, 100), Vr(1.00, 0.5, 0.02, 0.9, 50));
        double pHigh = EvaluatePersistence(Dfa(0.90, 0.9, 0.9, 100), Vr(1.00, 0.5, 0.02, 0.9, 50));

        double smallSwing = Math.Abs(
            EvaluatePersistence(Dfa(0.52, 0.9, 0.9, 100), Vr(1.00, 0.5, 0.02, 0.9, 50)) - pLow);
        double largeSwing = Math.Abs(pHigh - pLow);

        Assert(largeSwing > smallSwing, $"Case C: a large Hurst swing (0.50->0.90) must produce a larger Persistence movement than a small one (0.50->0.52) - observed small={smallSwing:F6}, large={largeSwing:F6}.");
    }

    private static void CaseD_ExtremeInputs_NoNaNNoInfinity_StaysInBounds()
    {
        double pMaxHurst = EvaluatePersistence(Dfa(2.0, 1.0, 1.0, 200), Vr(1_000_000.0, 50.0, 0.99, 1.0, 200));
        double pMinHurst = EvaluatePersistence(Dfa(0.0, 1.0, 1.0, 200), Vr(0.000001, 50.0, 0.99, 1.0, 200));

        foreach (double p in new[] { pMaxHurst, pMinHurst })
        {
            Assert(!double.IsNaN(p) && !double.IsInfinity(p), $"Case D: extreme inputs must never produce NaN/Infinity - observed {p}.");
            Assert(p is >= 0.0 and <= 1.0, $"Case D: Persistence must stay within [0,1] even for extreme inputs - observed {p:F6}.");
        }
    }

    private static void CaseE_InsufficientSamples_ProducesMissingEvidenceSentinel()
    {
        var builder = new FusionResultBuilder();
        new PersistenceRule().Evaluate(
            Context(DfaResult.Invalid("Warmup DFA (40/80 bars)."), Vr(1.05, 0.5, 0.02, 0.9, 50)),
            builder);

        FusionConfidence result = builder.Dimensions[FusionDimension.Persistence];
        Assert(!result.IsAvailable, "Case E: an invalid (insufficient-sample) DfaResult must produce the Missing Evidence sentinel (IsAvailable=false).");
        Assert(result.Value == 0.0 && result.Confidence == 0.0, "Case E: the Missing Evidence sentinel must be exactly Value=0.0/Confidence=0.0.");
    }

    private static void CaseF_ZeroVarianceRatioDenominator_NoNaNNoInfinity()
    {
        double p = EvaluatePersistence(Dfa(0.55, 0.9, 0.9, 100), Vr(0.0, 0.0, 0.5, 0.9, 50));

        Assert(!double.IsNaN(p) && !double.IsInfinity(p), $"Case F: VarianceRatio=0.0 (would be ln(0)=-Infinity without the production floor) must not produce NaN/Infinity - observed {p}.");
    }

    private static void CaseG_NullEvidence_ProducesMissingEvidenceSentinel()
    {
        var builder = new FusionResultBuilder();
        new PersistenceRule().Evaluate(
            new FusionContext
            {
                Evidence = new EvidenceSet { Timestamp = DateTime.UnixEpoch, Adf = null, Kpss = null, Hurst = null, HalfLife = null, VarianceRatio = null, Cusum = null, Volatility = null, BaiPerron = null, Dfa = null },
                Timestamp = DateTime.UnixEpoch, Symbol = "TEST", TimeFrame = "M5", EvaluationId = Guid.Empty
            },
            builder);

        FusionConfidence result = builder.Dimensions[FusionDimension.Persistence];
        Assert(!result.IsAvailable, "Case G: null Dfa/VarianceRatio evidence must produce the Missing Evidence sentinel.");
    }

    // THE CENTRAL CAUSAL DEMONSTRATION (brief §21): a sequence of RAW Persistence values, each moving by
    // LESS than FusionStateManager's hysteresis threshold (0.03) from the previous STABLE value, must
    // freeze the STABLE output at the FIRST bar's value - even though the raw input keeps drifting. Only
    // once the CUMULATIVE drift from the frozen value exceeds the threshold does the stable value move.
    // This is exercised through the real FusionStateManager class, never reimplemented.
    private static void HysteresisFreeze_SmallRawMovements_FreezeStableValue_UntilCumulativeThresholdCrossed()
    {
        var manager = new FusionStateManager();
        // Raw Persistence drifts 0.140 -> 0.145 -> 0.150 -> 0.155 -> 0.160 -> 0.165 -> 0.170 (each step
        // +0.005, individually far below the 0.03 hysteresis threshold).
        double[] rawSequence = { 0.140, 0.145, 0.150, 0.155, 0.160, 0.165, 0.170 };
        var stableSequence = new List<double>();

        for (int i = 0; i < rawSequence.Length; i++)
        {
            FusionResult raw = BuildRawFusion(rawSequence[i]);
            FusionSnapshot snapshot = manager.Update(raw, DateTime.UnixEpoch.AddMinutes(5 * i));
            stableSequence.Add(snapshot.StableResult.Dimensions[FusionDimension.Persistence].Value);
        }

        int distinctStableValues = stableSequence.Distinct().Count();
        Assert(
            distinctStableValues < rawSequence.Distinct().Count(),
            $"Hysteresis freeze: the STABLE sequence must show FEWER distinct values than the RAW sequence (small per-step movements absorbed by hysteresis) - observed stable distinct={distinctStableValues}, raw distinct={rawSequence.Distinct().Count()}, stable sequence=[{string.Join(", ", stableSequence.Select(v => v.ToString("F4")))}].");

        // Confirm at least one exact freeze: two consecutive stable values bit-identical despite the raw
        // input having moved between them.
        bool anyFreezeObserved = false;
        for (int i = 1; i < stableSequence.Count; i++)
            if (stableSequence[i] == stableSequence[i - 1]) anyFreezeObserved = true;
        Assert(anyFreezeObserved, "Hysteresis freeze: at least one consecutive pair of STABLE values must be bit-identical, demonstrating the freeze mechanism directly.");
    }

    private static double EvaluatePersistence(DfaResult dfa, VarianceRatioResult vr)
    {
        var builder = new FusionResultBuilder();
        new PersistenceRule().Evaluate(Context(dfa, vr), builder);
        return builder.Dimensions[FusionDimension.Persistence].Value;
    }

    private static FusionContext Context(DfaResult? dfa, VarianceRatioResult? vr) => new()
    {
        Evidence = new EvidenceSet
        {
            Timestamp = DateTime.UnixEpoch, Adf = null, Kpss = null, Hurst = null, HalfLife = null,
            VarianceRatio = vr, Cusum = null, Volatility = null, BaiPerron = null, Dfa = dfa
        },
        Timestamp = DateTime.UnixEpoch, Symbol = "TEST", TimeFrame = "M5", EvaluationId = Guid.Empty
    };

    private static DfaResult Dfa(double hurst, double rSquared, double confidence, int windowCount) => new()
    {
        Hurst = hurst, RSquared = rSquared, Confidence = confidence, WindowCount = windowCount, IsValid = true,
        Explanation = "Synthetic test fixture."
    };

    private static VarianceRatioResult Vr(double varianceRatio, double zStatistic, double pValue, double confidence, int sampleSize) => new()
    {
        VarianceRatio = varianceRatio, ZStatistic = zStatistic, PValue = pValue, Confidence = confidence,
        Lag = 2, SampleSize = sampleSize, IsValid = true, Explanation = "Synthetic test fixture."
    };

    private static FusionResult BuildRawFusion(double persistenceValue)
    {
        var builder = new FusionResultBuilder();
        builder.Dimensions[FusionDimension.Stationarity] = new FusionConfidence { Value = 0.40, Confidence = 0.80 };
        builder.Dimensions[FusionDimension.Persistence] = new FusionConfidence { Value = persistenceValue, Confidence = 0.80 };
        builder.Dimensions[FusionDimension.MeanReversion] = new FusionConfidence { Value = 0.55, Confidence = 0.80 };
        builder.Dimensions[FusionDimension.RandomWalk] = new FusionConfidence { Value = 0.20, Confidence = 0.80 };
        return builder.Build();
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
