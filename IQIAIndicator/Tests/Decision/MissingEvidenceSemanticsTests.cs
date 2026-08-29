using System.Globalization;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.Rules;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Fusion;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Fusion.State;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Regime.Evidence.DFA;
using IQIAIndicator.Engine.Regime.Evidence.HalfLife;
using IQIAIndicator.Engine.Regime.Evidence.VarianceRatio;

namespace IQIAIndicator.Tests.Decision;

/// <summary>
/// Sprint 14 / Audit findings DEC-01 and FUS-02.
///
/// The production "Missing Evidence" sentinel that Fusion rules emit when their required upstream
/// statistical evidence is null or invalid is <c>{ Value = 0.0, Confidence = 0.0, Explanation =
/// "Missing Evidence" }</c> - a PRESENT dictionary entry, not an absent key. Every Decision rule reads
/// this Value directly, and four of the five rules also invert it via <c>(1.0 - dimension.Value)</c>
/// to score the OPPOSITE hypothesis. Before this sprint's fix, that inversion turned "we have no
/// evidence" into "we have full confirmation of the opposite" - literally the same numeric
/// contribution as if a real measurement had come back with Value = 0.0 and Confidence = 1.0.
///
/// This file (a) proves the real Fusion rules emit exactly that sentinel shape, (b) reproduces the
/// bug end-to-end through a real Decision rule with hand-derived exact arithmetic (red before the
/// fix, green after), and (c) exercises the corrected semantics across the scenarios required by the
/// sprint: missing evidence, genuine low/high values, zero confidence on otherwise-available
/// evidence (which must NOT be treated as missing), high-confidence evidence, and a multi-dimension
/// combination where more than one dimension is simultaneously missing.
/// </summary>
public static class MissingEvidenceSemanticsTests
{
    public static void RunAll()
    {
        AssertRealFusionRuleSentinelShapeMatchesProductionContract();
        AssertMissingEvidenceDoesNotProduceArtificialDirectionalSignal();
        AssertGenuineLowValueIsUsedAsIs();
        AssertGenuineHighValueIsUsedAsIs();
        AssertZeroConfidenceWithAvailableEvidenceIsNotTreatedAsMissing();
        AssertHighConfidenceGenuineEvidenceIsUsedAsIs();
        AssertMultiDimensionMissingEvidenceCombination();
        AssertMissingEvidenceAvailabilitySurvivesFusionStateManagerSmoothing();
    }

    /// <summary>
    /// Confirms the exact shape this whole file's tests rely on is what the real, live Fusion rule
    /// actually emits (not an assumption baked into hand-built test doubles elsewhere in this file).
    /// </summary>
    private static void AssertRealFusionRuleSentinelShapeMatchesProductionContract()
    {
        var builder = new FusionResultBuilder();
        new PersistenceRule().Evaluate(
            new FusionContext
            {
                Evidence = new EvidenceSet
                {
                    Timestamp = DateTime.UnixEpoch,
                    Adf = null,
                    Kpss = null,
                    Hurst = null,
                    HalfLife = null,
                    VarianceRatio = null,
                    Cusum = null,
                    Volatility = null,
                    BaiPerron = null,
                    Dfa = null
                },
                Timestamp = DateTime.UnixEpoch,
                Symbol = string.Empty,
                TimeFrame = string.Empty,
                EvaluationId = Guid.Empty
            },
            builder);

        FusionConfidence sentinel = builder.Dimensions[FusionDimension.Persistence];
        Assert(sentinel.Value == 0.0, "Production sentinel Value must be exactly 0.0.");
        Assert(sentinel.Confidence == 0.0, "Production sentinel Confidence must be exactly 0.0.");
        Assert(sentinel.Explanation == "Missing Evidence", "Production sentinel Explanation must be exactly 'Missing Evidence'.");
        Assert(!sentinel.IsAvailable, "Production sentinel must be marked unavailable so Decision rules can distinguish it from a genuine zero measurement.");
    }

    /// <summary>
    /// The central DEC-01 / FUS-02 reproduction. All four non-Persistence dimensions are held at
    /// identical, realistic values; only Persistence carries the real production Missing Evidence
    /// sentinel. Hand-derived arithmetic (MeanRevertingRule.cs weights: MeanReversion=0.40,
    /// Stationarity=0.30, NonPersistence=0.20, StructuralStability=0.10; final blend
    /// 0.90*scientific+0.10*quality):
    ///
    ///   Pre-fix (bug):  NonPersistence term = 0.20*(1-0.0) = 0.20 -> scientific=0.68, final=0.676
    ///   Post-fix (neutral 0.5 substitution when unavailable):
    ///                   NonPersistence term = 0.20*(1-0.5) = 0.10 -> scientific=0.58, final=0.586
    ///
    /// This test asserts the POST-FIX value (0.586). Run before Part C's fix, it fails with the
    /// pre-fix value (0.676) - proving the bug is real and reproducible through a real Decision rule,
    /// not merely theorized.
    /// </summary>
    private static void AssertMissingEvidenceDoesNotProduceArtificialDirectionalSignal()
    {
        var builder = new FusionResultBuilder();
        builder.Dimensions[FusionDimension.MeanReversion] = Genuine(value: 0.6, confidence: 0.8);
        builder.Dimensions[FusionDimension.Stationarity] = Genuine(value: 0.6, confidence: 0.8);
        builder.Dimensions[FusionDimension.StructuralStability] = Genuine(value: 0.6, confidence: 0.8);
        builder.Dimensions[FusionDimension.Persistence] = MissingEvidenceSentinel();

        DecisionResult result = Evaluate(new MeanRevertingRule(), builder.Build());

        Assert(result.TriggeredRules.Contains(nameof(MeanRevertingRule)), "The rule must still fire on the available evidence.");
        AssertClose(0.586, result.Confidence,
            "Missing Persistence evidence must contribute a NEUTRAL term (as if Value=0.5), not a confirmed-opposite term (as if Value=0.0 with full confidence). " +
            "If this reads 0.676 instead, missing evidence is still being read as strong confirmed non-persistence - the DEC-01/FUS-02 bug is present.");
        Assert(result.Explanation.Contains("Final Score : 0.586", StringComparison.Ordinal), "The rule's own explanation string must reflect the neutral treatment.");
    }

    /// <summary>Scenario 2 (Part C): a genuine, present, low Value must be used exactly as measured - not perturbed by the missing-evidence handling.</summary>
    private static void AssertGenuineLowValueIsUsedAsIs()
    {
        var builder = new FusionResultBuilder();
        builder.Dimensions[FusionDimension.MeanReversion] = Genuine(value: 0.6, confidence: 0.8);
        builder.Dimensions[FusionDimension.Stationarity] = Genuine(value: 0.6, confidence: 0.8);
        builder.Dimensions[FusionDimension.StructuralStability] = Genuine(value: 0.6, confidence: 0.8);
        builder.Dimensions[FusionDimension.Persistence] = Genuine(value: 0.1, confidence: 0.9);

        DecisionResult result = Evaluate(new MeanRevertingRule(), builder.Build());

        // scientific = 0.40*0.6 + 0.30*0.6 + 0.20*(1-0.1) + 0.10*0.6 = 0.24+0.18+0.18+0.06 = 0.66
        // quality    = 0.40*0.8 + 0.30*0.8 + 0.20*0.9 + 0.10*0.8 = 0.32+0.24+0.18+0.08 = 0.82
        // final      = 0.9*0.66 + 0.1*0.82 = 0.594 + 0.082 = 0.676
        AssertClose(0.676, result.Confidence, "A genuine, present low Value must be used exactly as measured, unaffected by missing-evidence handling.");
    }

    /// <summary>Scenario 3 (Part C): a genuine, present, high Value must likewise be used exactly as measured.</summary>
    private static void AssertGenuineHighValueIsUsedAsIs()
    {
        var builder = new FusionResultBuilder();
        builder.Dimensions[FusionDimension.MeanReversion] = Genuine(value: 0.6, confidence: 0.8);
        builder.Dimensions[FusionDimension.Stationarity] = Genuine(value: 0.6, confidence: 0.8);
        builder.Dimensions[FusionDimension.StructuralStability] = Genuine(value: 0.6, confidence: 0.8);
        builder.Dimensions[FusionDimension.Persistence] = Genuine(value: 0.9, confidence: 0.9);

        DecisionResult result = Evaluate(new MeanRevertingRule(), builder.Build());

        // scientific = 0.40*0.6 + 0.30*0.6 + 0.20*(1-0.9) + 0.10*0.6 = 0.24+0.18+0.02+0.06 = 0.50
        // quality    = 0.40*0.8 + 0.30*0.8 + 0.20*0.9 + 0.10*0.8 = 0.82
        // final      = 0.9*0.50 + 0.1*0.82 = 0.45 + 0.082 = 0.532
        AssertClose(0.532, result.Confidence, "A genuine, present high Value must be used exactly as measured, unaffected by missing-evidence handling.");
    }

    /// <summary>
    /// Scenario 4 (Part C): confidence == 0 on evidence that IS available (a real but maximally
    /// unreliable measurement) must NOT be silently reinterpreted as "missing". Only the explicit
    /// IsAvailable flag - never a Confidence == 0 heuristic - may trigger the neutral substitution.
    /// This is the scenario that proves the fix is keyed on the correct signal.
    /// </summary>
    private static void AssertZeroConfidenceWithAvailableEvidenceIsNotTreatedAsMissing()
    {
        var builder = new FusionResultBuilder();
        builder.Dimensions[FusionDimension.MeanReversion] = Genuine(value: 0.6, confidence: 0.8);
        builder.Dimensions[FusionDimension.Stationarity] = Genuine(value: 0.6, confidence: 0.8);
        builder.Dimensions[FusionDimension.StructuralStability] = Genuine(value: 0.6, confidence: 0.8);
        // Real measurement (IsAvailable = true, via Genuine()), Value = 0.7, but Confidence = 0.0.
        builder.Dimensions[FusionDimension.Persistence] = Genuine(value: 0.7, confidence: 0.0);

        DecisionResult result = Evaluate(new MeanRevertingRule(), builder.Build());

        // scientific = 0.40*0.6 + 0.30*0.6 + 0.20*(1-0.7) + 0.10*0.6 = 0.24+0.18+0.06+0.06 = 0.54
        // quality    = 0.40*0.8 + 0.30*0.8 + 0.20*0.0 + 0.10*0.8 = 0.32+0.24+0.00+0.08 = 0.64
        // final      = 0.9*0.54 + 0.1*0.64 = 0.486 + 0.064 = 0.550
        // Compare against AssertMissingEvidenceDoesNotProduceArtificialDirectionalSignal's 0.586:
        // a different, non-neutral scientific contribution (0.06 vs the missing-evidence case's 0.10),
        // proving this path is NOT collapsed onto the missing-evidence handling.
        AssertClose(0.550, result.Confidence, "Zero confidence on genuinely available evidence must use the real Value (0.7) as-is, not the neutral missing-evidence substitute.");
    }

    /// <summary>Scenario 5 (Part C): high-confidence genuine evidence, used as-is.</summary>
    private static void AssertHighConfidenceGenuineEvidenceIsUsedAsIs()
    {
        var builder = new FusionResultBuilder();
        builder.Dimensions[FusionDimension.MeanReversion] = Genuine(value: 0.6, confidence: 0.8);
        builder.Dimensions[FusionDimension.Stationarity] = Genuine(value: 0.6, confidence: 0.8);
        builder.Dimensions[FusionDimension.StructuralStability] = Genuine(value: 0.6, confidence: 0.8);
        builder.Dimensions[FusionDimension.Persistence] = Genuine(value: 0.3, confidence: 0.99);

        DecisionResult result = Evaluate(new MeanRevertingRule(), builder.Build());

        // scientific = 0.40*0.6 + 0.30*0.6 + 0.20*(1-0.3) + 0.10*0.6 = 0.24+0.18+0.14+0.06 = 0.62
        // quality    = 0.40*0.8 + 0.30*0.8 + 0.20*0.99 + 0.10*0.8 = 0.32+0.24+0.198+0.08 = 0.838
        // final      = 0.9*0.62 + 0.1*0.838 = 0.558 + 0.0838 = 0.6418
        AssertClose(0.6418, result.Confidence, "High-confidence genuine evidence must be used exactly as measured.");
    }

    /// <summary>
    /// Scenario 6 (Part C): a combination where TWO of four dimensions read by the same rule are
    /// simultaneously the real Missing Evidence sentinel. Uses StructuralBreakRule, the rule where
    /// ALL FOUR terms are inverted (the worst-case shape for this bug), to prove the fix holds even
    /// when the missing-evidence handling has to fire more than once within a single evaluation.
    /// </summary>
    private static void AssertMultiDimensionMissingEvidenceCombination()
    {
        var builder = new FusionResultBuilder();
        builder.Dimensions[FusionDimension.MeanReversion] = Genuine(value: 0.7, confidence: 0.9);
        builder.Dimensions[FusionDimension.Stationarity] = Genuine(value: 0.7, confidence: 0.9);
        builder.Dimensions[FusionDimension.Persistence] = MissingEvidenceSentinel();
        builder.Dimensions[FusionDimension.StructuralStability] = MissingEvidenceSentinel();

        DecisionResult result = Evaluate(new StructuralBreakRule(), builder.Build());

        // StructuralBreakRule.cs weights: StructuralInstability=0.40, NonPersistence=0.30,
        // NonMeanReversion=0.20, NonStationarity=0.10 (all inverted).
        // scientific = 0.40*(1-0.5) + 0.30*(1-0.5) + 0.20*(1-0.7) + 0.10*(1-0.7)
        //            = 0.40*0.5 + 0.30*0.5 + 0.20*0.3 + 0.10*0.3 = 0.20+0.15+0.06+0.03 = 0.44
        // quality    = 0.40*0.0 + 0.30*0.0 + 0.20*0.9 + 0.10*0.9 = 0.00+0.00+0.18+0.09 = 0.27
        // final      = 0.9*0.44 + 0.1*0.27 = 0.396 + 0.027 = 0.423
        AssertClose(0.423, result.Confidence, "Two simultaneously-missing dimensions must each contribute their neutral term independently, not compound into an inflated signal.");
    }

    /// <summary>
    /// Proves the fix survives the ACTUAL production path Decision consumes: IQIAIndicator.cs feeds
    /// DecisionEngine the STABLE (smoothed) result from FusionStateManager.Update, never the raw
    /// EvidenceFusionEngine.Fuse output directly. Without propagating IsAvailable through both the
    /// bar-1 initial pass-through and the bar-2+ EMA smoothing branch, a Decision rule fed the stable
    /// result would silently regress to the pre-fix behavior even with FusionConfidence.IsAvailable
    /// and the Decision-rule EffectiveValue fix both in place.
    /// </summary>
    private static void AssertMissingEvidenceAvailabilitySurvivesFusionStateManagerSmoothing()
    {
        var manager = new FusionStateManager();

        var rawBar1 = new FusionResultBuilder();
        rawBar1.Dimensions[FusionDimension.Stationarity] = Genuine(0.6, 0.8);
        rawBar1.Dimensions[FusionDimension.MeanReversion] = Genuine(0.6, 0.8);
        rawBar1.Dimensions[FusionDimension.Persistence] = MissingEvidenceSentinel();
        rawBar1.Dimensions[FusionDimension.RandomWalk] = Genuine(0.6, 0.8);

        var snapshot1 = manager.Update(rawBar1.Build(), DateTime.UnixEpoch);
        FusionConfidence persistenceAfterBar1 = snapshot1.StableResult.Dimensions[FusionDimension.Persistence];
        Assert(!persistenceAfterBar1.IsAvailable, "Bar 1: the stable result's Persistence dimension must still be marked unavailable after passing through FusionStateManager (initial pass-through path).");

        var rawBar2 = new FusionResultBuilder();
        rawBar2.Dimensions[FusionDimension.Stationarity] = Genuine(0.6, 0.8);
        rawBar2.Dimensions[FusionDimension.MeanReversion] = Genuine(0.6, 0.8);
        rawBar2.Dimensions[FusionDimension.Persistence] = MissingEvidenceSentinel();
        rawBar2.Dimensions[FusionDimension.RandomWalk] = Genuine(0.6, 0.8);

        var snapshot2 = manager.Update(rawBar2.Build(), DateTime.UnixEpoch.AddSeconds(1));
        FusionConfidence persistenceAfterBar2 = snapshot2.StableResult.Dimensions[FusionDimension.Persistence];
        Assert(!persistenceAfterBar2.IsAvailable, "Bar 2: the stable result's Persistence dimension must still be marked unavailable after passing through FusionStateManager's EMA smoothing branch.");

        // Feed the real stable (smoothed) result - not a hand-built FusionResult - into a Decision
        // rule, proving the whole chain (Fusion -> FusionStateManager -> Decision) is honest, not
        // just the Fusion -> Decision shortcut the other tests in this file use.
        DecisionResult result = Evaluate(new MeanRevertingRule(), snapshot2.StableResult);
        Assert(result.TriggeredRules.Contains(nameof(MeanRevertingRule)), "The rule must still fire when fed the real stable result.");
    }

    private static DecisionResult Evaluate(IDecisionRule rule, FusionResult fusionResult)
    {
        var engine = new DecisionEngine([rule]);
        return engine.Evaluate(new DecisionContext
        {
            FusionResult = fusionResult,
            Evidence = null!
        });
    }

    private static FusionConfidence Genuine(double value, double confidence) => new()
    {
        Value = value,
        Confidence = confidence,
        Explanation = "Genuine evidence (test fixture)."
    };

    private static FusionConfidence MissingEvidenceSentinel() => new()
    {
        Value = 0.0,
        Confidence = 0.0,
        Explanation = "Missing Evidence",
        IsAvailable = false
    };

    private static void AssertClose(double expected, double actual, string message)
    {
        if (Math.Abs(expected - actual) > 1e-9)
            throw new InvalidOperationException($"{message} Expected={expected.ToString("F6", CultureInfo.InvariantCulture)}; Actual={actual.ToString("F6", CultureInfo.InvariantCulture)}.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
