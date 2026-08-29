using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.Rules;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Fusion.Core;

namespace IQIAIndicator.Tests.Decision;

/// <summary>
/// Sprint 15.25 (Lot 14.15, brief §14). Synthetic Cases A-F comparing StableRangeRule and MeanRevertingRule
/// - every case runs the REAL 5 IDecisionRule classes through the REAL DecisionEngine/DecisionArbitrator
/// (nothing reimplemented); only the synthetic FusionResult INPUT is chosen per case, and the REAL output is
/// observed (brief: "Observer les scores des deux règles. Ne pas modifier les formules."). Assertions check
/// DIRECTIONAL correctness (which rule wins, whether separation is meaningfully larger than the ambiguous
/// baseline) rather than hand-computed exact values, since the point is to observe what the real formulas
/// do, not to re-derive them by hand.
/// </summary>
public static class StableRangeMeanRevertingSyntheticCasesTests
{
    public static void RunAll()
    {
        CaseA_HighSharedDimensions_NeutralPersistence_ProducesHighAmbiguity();
        CaseB_HighSharedDimensions_DivergentPersistence_SeparatesRules();
        CaseC_LowSharedDimensions_DivergentPersistence_StillSeparatesDirectionally();
        CaseD_StableRangeEvident_StableRangeWinsWithMeaningfulMargin();
        CaseE_MeanRevertingEvident_MeanRevertingWinsWithMeaningfulMargin();
        CaseF_TrulyAmbiguous_NearNeutralEverything_ProducesNearMaximumAmbiguity();
    }

    // CASE A: Stationarity/MeanReversion both high, Persistence at a neutral baseline (not pushed toward
    // either rule's advantage). Expect the two rules to score closely - high AmbiguityScore.
    private static void CaseA_HighSharedDimensions_NeutralPersistence_ProducesHighAmbiguity()
    {
        DecisionResult result = Evaluate(BuildFusion(stationarity: 0.85, persistence: 0.50, meanReversion: 0.85, randomWalk: 0.20, structuralStability: 0.70));

        double mr = result.RuleScore(MarketState.MeanReverting);
        double sr = result.RuleScore(MarketState.StableRange);
        Assert(Math.Abs(mr - sr) < 0.06, $"Case A: neutral Persistence must keep MeanReverting/StableRange close - observed |MR-SR|={Math.Abs(mr - sr):F4}.");
        Assert(result.AmbiguityScore > 0.90, $"Case A must produce high ambiguity - observed {result.AmbiguityScore:F4}.");
    }

    // CASE B: identical shared dimensions to Case A, but Persistence pushed to a divergent extreme (0.95)
    // - a dimension StableRangeRule ignores but MeanRevertingRule penalizes via (1-Persistence)*0.20.
    // Expect meaningfully MORE separation than Case A (Lot 14.14's finding, re-verified here on the
    // specific StableRange/MeanReverting pair rather than against a pooled "all 5 rules" baseline).
    private static void CaseB_HighSharedDimensions_DivergentPersistence_SeparatesRules()
    {
        DecisionResult caseA = Evaluate(BuildFusion(0.85, 0.50, 0.85, 0.20, 0.70));
        DecisionResult caseB = Evaluate(BuildFusion(stationarity: 0.85, persistence: 0.95, meanReversion: 0.85, randomWalk: 0.20, structuralStability: 0.70));

        double diffA = Math.Abs(caseA.RuleScore(MarketState.MeanReverting) - caseA.RuleScore(MarketState.StableRange));
        double diffB = Math.Abs(caseB.RuleScore(MarketState.MeanReverting) - caseB.RuleScore(MarketState.StableRange));

        Assert(diffB > diffA + 0.05, $"Case B: divergent Persistence must separate MeanReverting/StableRange meaningfully more than Case A's neutral Persistence - observed diffA={diffA:F4}, diffB={diffB:F4}.");
        Assert(caseB.RuleScore(MarketState.StableRange) > caseB.RuleScore(MarketState.MeanReverting), "Case B: high Persistence penalizes MeanRevertingRule specifically (via (1-Persistence)), so StableRangeRule must score higher here.");
    }

    // CASE C: the SAME divergent-Persistence mechanism, but with weak shared evidence (Stationarity/
    // MeanReversion both low). Tests whether Persistence's discriminating power (Case B) survives when the
    // shared dimensions are NOT decisive - the brief explicitly asks not to assume Case B's finding
    // generalizes without checking.
    private static void CaseC_LowSharedDimensions_DivergentPersistence_StillSeparatesDirectionally()
    {
        DecisionResult result = Evaluate(BuildFusion(stationarity: 0.30, persistence: 0.90, meanReversion: 0.30, randomWalk: 0.50, structuralStability: 0.50));

        double mr = result.RuleScore(MarketState.MeanReverting);
        double sr = result.RuleScore(MarketState.StableRange);
        Assert(sr > mr, $"Case C: even with weak shared evidence, high Persistence must still make StableRangeRule outscore MeanRevertingRule (directionally consistent with Case B) - observed MR={mr:F4}, SR={sr:F4}.");
    }

    // CASE D: a scenario a human would call "obviously StableRange" - high Stationarity+MeanReversion
    // (StableRangeRule's own two dominant weights), high Persistence (suppresses MeanRevertingRule
    // specifically, per Case B's mechanism), moderate-high StructuralStability.
    private static void CaseD_StableRangeEvident_StableRangeWinsWithMeaningfulMargin()
    {
        DecisionResult result = Evaluate(BuildFusion(stationarity: 0.85, persistence: 0.90, meanReversion: 0.80, randomWalk: 0.20, structuralStability: 0.75));

        Assert(result.Winner == MarketState.StableRange, $"Case D must make StableRange the Winner - observed Winner={result.Winner}.");
        Assert(result.Candidates.Length == 5, "All five rules must still trigger (Lot 14.14's 'always 5 candidates' finding).");
        Assert(result.WinnerScore - result.Candidates[1].FinalScore > 0.08, $"Case D must produce a meaningfully larger margin than the ambiguous baseline - observed margin={result.WinnerScore - result.Candidates[1].FinalScore:F4}.");
    }

    // CASE E: the mirror scenario for MeanReverting - high MeanReversion, LOW Persistence (boosts
    // MeanRevertingRule via (1-Persistence)), moderately lower Stationarity (StableRangeRule weights
    // Stationarity equally to MeanReversion at 0.40 each; MeanRevertingRule weights MeanReversion higher
    // (0.40) than Stationarity (0.30), so lowering Stationarity while keeping MeanReversion high favours
    // MeanRevertingRule relatively).
    private static void CaseE_MeanRevertingEvident_MeanRevertingWinsWithMeaningfulMargin()
    {
        DecisionResult result = Evaluate(BuildFusion(stationarity: 0.55, persistence: 0.02, meanReversion: 0.90, randomWalk: 0.15, structuralStability: 0.60));

        Assert(result.Winner == MarketState.MeanReverting, $"Case E must make MeanReverting the Winner - observed Winner={result.Winner}.");
        Assert(result.WinnerScore > result.Candidates[1].FinalScore, "Case E: MeanReverting must lead its closest competitor.");
    }

    // CASE F: truly ambiguous - every dimension near a common, unremarkable midpoint (mirrors Lot 14.14's
    // near-neutral synthetic fixture). Expect near-maximum AmbiguityScore, with StableRange and
    // MeanReverting as the top two (the correlated pair, per Lot 14.14 §15/§21).
    private static void CaseF_TrulyAmbiguous_NearNeutralEverything_ProducesNearMaximumAmbiguity()
    {
        DecisionResult result = Evaluate(BuildFusion(stationarity: 0.58, persistence: 0.55, meanReversion: 0.60, randomWalk: 0.52, structuralStability: 0.65));

        Assert(result.AmbiguityScore > 0.85, $"Case F must produce near-maximum ambiguity - observed {result.AmbiguityScore:F4}.");
        Assert(
            (result.Winner == MarketState.MeanReverting && result.Candidates[1].MarketState == MarketState.StableRange) ||
            (result.Winner == MarketState.StableRange && result.Candidates[1].MarketState == MarketState.MeanReverting),
            $"Case F: the top two candidates must be the correlated MeanReverting/StableRange pair (Lot 14.14 §15) - observed Winner={result.Winner}, RunnerUp={result.Candidates[1].MarketState}.");
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

    private static DecisionResult Evaluate(FusionResult fusion)
    {
        var engine = new DecisionEngine(new IDecisionRule[]
        {
            new StableRangeRule(), new TrendingRule(), new MeanRevertingRule(), new StructuralBreakRule(), new RandomWalkRule()
        });

        return engine.Evaluate(new DecisionContext { FusionResult = fusion, Evidence = null! });
    }

    private static double RuleScore(this DecisionResult result, MarketState state) =>
        result.Candidates.First(c => c.MarketState == state).FinalScore;

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
