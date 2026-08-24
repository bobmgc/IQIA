using IQIAIndicator.Engine.Decision.Arbitration;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.Rules;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Fusion.Core;

namespace IQIAIndicator.Tests.Decision;

/// <summary>
/// Sprint 15.25 (Lot 14.14). Synthetic, observation-only fixtures for the AmbiguityScore investigation
/// (brief §21/§22) - NEW cases only, not already covered by <see cref="DecisionArbitrationTests"/> (which
/// already proves clear-winner/close-candidates/no-candidates behaviour). Every formula exercised here is
/// the REAL production code (<see cref="DecisionArbitrator"/>, the 5 real <c>IDecisionRule</c>s) - nothing
/// here reimplements a scoring formula; these tests only choose synthetic INPUTS and observe REAL outputs
/// (brief §22: "observer ce que fait réellement la formule, pas déterminer ce qu'elle devrait faire").
/// </summary>
public static class AmbiguityScoreSemanticSyntheticTests
{
    public static void RunAll()
    {
        SingleCandidate_HighScore_ProducesLowAmbiguity();
        SingleCandidate_LowScore_ProducesHighAmbiguity();
        IdenticalScores_ProduceExactMaximumAmbiguity();
        ExtremeScores_NoNaNNoInfinity_ClampHoldsAtBoundaries();
        RealisticNearNeutralFusion_UsingRealDecisionRules_ProducesHighAmbiguity();
        JointlyExtremeSharedDimensions_UsingRealDecisionRules_DoesNotReduceAmbiguity();
        DivergingOnPersistence_UsingRealDecisionRules_DoesReduceAmbiguity();
        SameFusionInput_EvaluatedTwice_ProducesIdenticalAmbiguity();
    }

    // CAS D (brief §22): a single candidate has no real "runner-up" to compare against, yet
    // DecisionArbitrator.Arbitrate defaults RunnerUpScore to 0.0 (never null-propagated), so
    // Difference = WinnerScore - 0.0 = WinnerScore. OBSERVED: a lone, strongly-scored candidate reads as
    // LOW ambiguity - the metric is not "was there a real competitor", it is "how far is the winner from
    // zero when no competitor exists". Documented as a semantic edge case (report §Semantic Analysis),
    // never corrected here (brief §23/§27: no production logic touched).
    private static void SingleCandidate_HighScore_ProducesLowAmbiguity()
    {
        DecisionResult result = Arbitrate(Candidate(MarketState.MeanReverting, 0.90));

        Assert(result.Candidates.Length == 1, "Exactly one candidate must survive.");
        AssertClose(0.90, result.Difference(), "Difference must equal WinnerScore - 0.0 for a lone candidate.");
        AssertClose(0.10, result.AmbiguityScore, "A lone, strong candidate must read as LOW ambiguity (observed, not a correctness claim).");
    }

    private static void SingleCandidate_LowScore_ProducesHighAmbiguity()
    {
        DecisionResult result = Arbitrate(Candidate(MarketState.RandomWalk, 0.10));

        AssertClose(0.10, result.Difference(), "Difference must equal WinnerScore for a lone weak candidate.");
        AssertClose(0.90, result.AmbiguityScore, "A lone, weak candidate must read as HIGH ambiguity - inverted from the strong-lone-candidate case above.");
    }

    // CAS C: bit-identical scores collapse Difference to exactly 0.0 - AmbiguityScore = Clamp(1-0,0,1) = 1.0
    // exactly, not merely "close to 1".
    private static void IdenticalScores_ProduceExactMaximumAmbiguity()
    {
        DecisionResult result = Arbitrate(
            Candidate(MarketState.StableRange, 0.65),
            Candidate(MarketState.Trending, 0.65));

        Assert(result.Difference() == 0.0, "Bit-identical FinalScores must produce an exact zero Difference.");
        Assert(result.AmbiguityScore == 1.0, "Zero Difference must produce an exact 1.0 AmbiguityScore (Clamp boundary, not an approximation).");
    }

    // Clamp boundaries: a Difference far outside [-1,1] (candidates outside the normal [0,1] convention)
    // must still clamp cleanly to [0,1] - never NaN/Infinity, confirming the formula's own guard, not a
    // contrived crash scenario (brief §21).
    private static void ExtremeScores_NoNaNNoInfinity_ClampHoldsAtBoundaries()
    {
        DecisionResult result = Arbitrate(
            Candidate(MarketState.Trending, 5.0),
            Candidate(MarketState.RandomWalk, -3.0));

        Assert(!double.IsNaN(result.AmbiguityScore) && !double.IsInfinity(result.AmbiguityScore), "AmbiguityScore must never be NaN/Infinity.");
        Assert(result.AmbiguityScore == 0.0, "A Difference far above 1.0 must clamp to a 0.0 AmbiguityScore, not merely a small one.");

        DecisionResult zeroWinner = Arbitrate(Candidate(MarketState.MeanReverting, 0.0));
        Assert(zeroWinner.AmbiguityScore == 1.0, "A lone candidate at exactly 0.0 must clamp to a 1.0 AmbiguityScore.");
    }

    // The central hypothesis test (brief §20 categories B/C/D): feed the REAL 5 DecisionRule classes a
    // synthetic-but-plausible FusionResult where StructuralStability sits near 1.0 (Lot 14.14's own
    // empirical finding: StructuralStabilityRule defaults to Value=1.0/Confidence=1.0 during warm-up and
    // trends there whenever FusionProfileAnalyzer.BehaviourConsistency is high - see the report's
    // Compression Trace) and the other four dimensions sit at a moderate, UNremarkable value (0.55-0.60,
    // not a strong directional read either way). OBSERVED: this alone, using nothing but real production
    // weights/formulas, reproduces a high AmbiguityScore - directly demonstrating the mechanism traced in
    // the report, not merely asserting it in prose.
    private static void RealisticNearNeutralFusion_UsingRealDecisionRules_ProducesHighAmbiguity()
    {
        FusionResult fusion = BuildFusion(
            stationarity: 0.58, persistence: 0.55, meanReversion: 0.60, randomWalk: 0.52, structuralStability: 0.97);

        DecisionResult result = EvaluateAllRealRules(fusion);

        Assert(result.Candidates.Length == 5, "All five real Decision rules must trigger on a fully-populated FusionResult (confirms the 'always-fresh-builder' finding: every rule's own Evaluate always beats its own fresh 0.0 baseline).");
        Assert(result.AmbiguityScore > 0.85, $"Near-neutral, clustered Fusion dimensions must reproduce a high AmbiguityScore using REAL rule weights - observed {result.AmbiguityScore:F4}.");
    }

    // Root-cause probe (brief §20 categories B/C): a first attempt pushed Stationarity+MeanReversion+
    // RandomWalk jointly to "decisive" extremes (Stat=0.92, MR=0.90, RW=0.08), expecting AmbiguityScore to
    // drop versus the near-neutral case. It did NOT drop - it INCREASED slightly (0.9856 -> 0.9919, observed
    // by running this exact scenario before writing this assertion). Root cause, traced by hand from the
    // real weight constants: StableRangeRule (Stat=0.40, MR=0.40, StructuralStability=0.20) and
    // MeanRevertingRule (MR=0.40, Stat=0.30, (1-Persistence)=0.20, StructuralStability=0.10) share ~70-80%
    // of their weight mass on the SAME two dimensions (Stationarity, MeanReversion) - pushing those two
    // dimensions jointly higher raises BOTH rules' FinalScore almost in lockstep, keeping them close
    // together as Winner/RunnerUp regardless of how "decisive" the input looks to a human reader. This is
    // documented here as the OBSERVED behaviour, not corrected (brief §23/§27).
    private static void JointlyExtremeSharedDimensions_UsingRealDecisionRules_DoesNotReduceAmbiguity()
    {
        FusionResult neutral = BuildFusion(0.58, 0.55, 0.60, 0.52, 0.97);
        FusionResult jointlyExtreme = BuildFusion(
            stationarity: 0.92, persistence: 0.10, meanReversion: 0.90, randomWalk: 0.08, structuralStability: 0.97);

        DecisionResult neutralResult = EvaluateAllRealRules(neutral);
        DecisionResult extremeResult = EvaluateAllRealRules(jointlyExtreme);

        Assert(
            extremeResult.Winner == MarketState.StableRange && extremeResult.Candidates[1].MarketState == MarketState.MeanReverting,
            $"Jointly extreme Stationarity+MeanReversion must make StableRangeRule and MeanRevertingRule the top two candidates - observed Winner={extremeResult.Winner}, RunnerUp={extremeResult.Candidates[1].MarketState}.");
        Assert(
            extremeResult.AmbiguityScore >= neutralResult.AmbiguityScore - 1e-6,
            $"Jointly extreme SHARED dimensions must NOT meaningfully reduce AmbiguityScore relative to the near-neutral case, because the top two rules move together " +
            $"(neutral={neutralResult.AmbiguityScore:F4}, jointlyExtreme={extremeResult.AmbiguityScore:F4}).");
    }

    // The corrected demonstration: diverging on Persistence - a dimension StableRangeRule does not use at
    // all (weight 0.0) but MeanRevertingRule weights at 0.20 via (1-Persistence) - DOES separate the top
    // two candidates, because it moves one rule's score without moving the other's. This confirms the
    // compression traced above is specifically a SHARED-WEIGHT correlation between structurally adjacent
    // rules, not an unconditional property of the arbitration formula itself.
    private static void DivergingOnPersistence_UsingRealDecisionRules_DoesReduceAmbiguity()
    {
        FusionResult jointlyExtreme = BuildFusion(0.92, 0.10, 0.90, 0.08, 0.97);
        FusionResult divergingOnPersistence = BuildFusion(
            stationarity: 0.92, persistence: 0.95, meanReversion: 0.90, randomWalk: 0.08, structuralStability: 0.97);

        DecisionResult jointResult = EvaluateAllRealRules(jointlyExtreme);
        DecisionResult divergedResult = EvaluateAllRealRules(divergingOnPersistence);

        Assert(
            divergedResult.AmbiguityScore < jointResult.AmbiguityScore - 0.05,
            $"Raising Persistence (a dimension StableRangeRule ignores but MeanRevertingRule penalizes via (1-Persistence)) must meaningfully separate the top two candidates " +
            $"(jointlyExtreme={jointResult.AmbiguityScore:F4}, divergingOnPersistence={divergedResult.AmbiguityScore:F4}).");
    }

    private static void SameFusionInput_EvaluatedTwice_ProducesIdenticalAmbiguity()
    {
        FusionResult fusion = BuildFusion(0.70, 0.45, 0.65, 0.30, 0.90);

        DecisionResult first = EvaluateAllRealRules(fusion);
        DecisionResult second = EvaluateAllRealRules(fusion);

        Assert(first.AmbiguityScore == second.AmbiguityScore, "Identical FusionResult input must produce a bit-identical AmbiguityScore across two independent evaluations.");
        Assert(first.Winner == second.Winner, "Identical FusionResult input must produce an identical Winner.");
    }

    private static FusionResult BuildFusion(
        double stationarity, double persistence, double meanReversion, double randomWalk, double structuralStability)
    {
        var builder = new FusionResultBuilder();
        builder.Dimensions[FusionDimension.Stationarity] = new FusionConfidence { Value = stationarity, Confidence = 0.80 };
        builder.Dimensions[FusionDimension.Persistence] = new FusionConfidence { Value = persistence, Confidence = 0.80 };
        builder.Dimensions[FusionDimension.MeanReversion] = new FusionConfidence { Value = meanReversion, Confidence = 0.80 };
        builder.Dimensions[FusionDimension.RandomWalk] = new FusionConfidence { Value = randomWalk, Confidence = 0.80 };
        builder.Dimensions[FusionDimension.StructuralStability] = new FusionConfidence { Value = structuralStability, Confidence = 0.80 };
        return builder.Build();
    }

    private static DecisionResult EvaluateAllRealRules(FusionResult fusion)
    {
        var engine = new DecisionEngine(new IDecisionRule[]
        {
            new StableRangeRule(), new TrendingRule(), new MeanRevertingRule(), new StructuralBreakRule(), new RandomWalkRule()
        });

        return engine.Evaluate(new DecisionContext
        {
            FusionResult = fusion,
            Evidence = null!
        });
    }

    private static DecisionResult Arbitrate(params DecisionCandidate[] candidates) =>
        new DecisionArbitrator().Arbitrate(candidates);

    private static DecisionCandidate Candidate(MarketState marketState, double finalScore) => new()
    {
        MarketState = marketState,
        ScientificScore = finalScore,
        QualityScore = finalScore,
        FinalScore = finalScore,
        Explanation = string.Empty
    };

    private static double Difference(this DecisionResult result) =>
        result.Candidates.Length == 0 ? 0.0 : result.WinnerScore - (result.Candidates.Length > 1 ? result.Candidates[1].FinalScore : 0.0);

    private static void AssertClose(double expected, double actual, string message)
    {
        if (Math.Abs(expected - actual) > 1e-9)
            throw new InvalidOperationException($"{message} Expected={expected:F6}; Actual={actual:F6}.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
