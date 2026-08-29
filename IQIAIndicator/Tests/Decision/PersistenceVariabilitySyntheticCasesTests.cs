using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.Rules;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Fusion.Core;

namespace IQIAIndicator.Tests.Decision;

/// <summary>
/// Sprint 15.25 (Lot 14.16, brief §22). Synthetic Cases A-F verifying the MATHEMATICAL behaviour of
/// Persistence as a discriminant between MeanRevertingRule/StableRangeRule - every case runs the REAL
/// classes (nothing reimplemented); only synthetic inputs are chosen, real outputs observed. No weight is
/// modified anywhere in this file.
/// </summary>
public static class PersistenceVariabilitySyntheticCasesTests
{
    public static void RunAll()
    {
        CasesABC_LowMediumHighPersistence_ScoreDifferenceDecreasesMonotonically();
        CaseD_PersistenceSweep_QuantifiesAchievableRange();
        CaseE_IdenticalPersistence_StructuralStabilityStillCreatesSmallerSeparation();
        CaseF_DivergentPersistence_SeparationIndependentOfSharedDimensionLevel();
    }

    // CASES A/B/C: low (0.10), medium (0.50), high (0.90) Persistence, identical shared dimensions.
    // Expect ScoreDifference(MR-SR) to decrease monotonically as Persistence rises (mirrors the real-data
    // decile finding, Lot 14.16 report §13).
    private static void CasesABC_LowMediumHighPersistence_ScoreDifferenceDecreasesMonotonically()
    {
        double diffLow = ScoreDifference(BuildFusion(0.60, persistence: 0.10, 0.60, 0.30, 0.65));
        double diffMedium = ScoreDifference(BuildFusion(0.60, persistence: 0.50, 0.60, 0.30, 0.65));
        double diffHigh = ScoreDifference(BuildFusion(0.60, persistence: 0.90, 0.60, 0.30, 0.65));

        Assert(diffLow > diffMedium, $"Case A->B: raising Persistence from 0.10 to 0.50 must reduce ScoreDifference - observed low={diffLow:F4}, medium={diffMedium:F4}.");
        Assert(diffMedium > diffHigh, $"Case B->C: raising Persistence from 0.50 to 0.90 must further reduce ScoreDifference - observed medium={diffMedium:F4}, high={diffHigh:F4}.");
    }

    // CASE D: sweep Persistence across its full range, shared dimensions fixed. Quantifies the achievable
    // swing in ScoreDifference directly, rather than asserting a specific value.
    private static void CaseD_PersistenceSweep_QuantifiesAchievableRange()
    {
        double[] sweep = { 0.0, 0.2, 0.4, 0.6, 0.8, 1.0 };
        var diffs = new List<double>();
        foreach (double p in sweep)
            diffs.Add(ScoreDifference(BuildFusion(0.60, p, 0.60, 0.30, 0.65)));

        for (int i = 1; i < diffs.Count; i++)
            Assert(diffs[i] < diffs[i - 1], $"Case D: ScoreDifference must decrease at every step of the Persistence sweep - observed diffs=[{string.Join(", ", diffs.Select(d => d.ToString("F4")))}].");

        double range = diffs.Max() - diffs.Min();
        Assert(range > 0.10, $"Case D: the full Persistence sweep [0,1] must produce a meaningfully large achievable ScoreDifference range - observed range={range:F4}.");
    }

    // CASE E: Persistence held IDENTICAL (0.50) across two scenarios, but StructuralStability diverges
    // (0.20 vs 0.95) - StableRangeRule weights StructuralStability at 0.20 vs MeanRevertingRule's 0.10, so
    // this alone creates SOME separation even with Persistence fixed. Expect this effect to be real but
    // SMALLER in magnitude than Persistence's own full-range effect (Case D), consistent with the real-data
    // finding that StructuralStability is a weaker discriminant than Persistence (Lot 14.15 §16).
    private static void CaseE_IdenticalPersistence_StructuralStabilityStillCreatesSmallerSeparation()
    {
        double diffLowStability = ScoreDifference(BuildFusion(0.60, persistence: 0.50, 0.60, 0.30, structuralStability: 0.20));
        double diffHighStability = ScoreDifference(BuildFusion(0.60, persistence: 0.50, 0.60, 0.30, structuralStability: 0.95));

        double structuralStabilityEffect = Math.Abs(diffHighStability - diffLowStability);
        Assert(structuralStabilityEffect > 0.01, $"Case E: StructuralStability divergence must create SOME real separation even with Persistence fixed - observed effect={structuralStabilityEffect:F4}.");

        double persistenceFullRangeEffect = Math.Abs(
            ScoreDifference(BuildFusion(0.60, 0.0, 0.60, 0.30, 0.65)) - ScoreDifference(BuildFusion(0.60, 1.0, 0.60, 0.30, 0.65)));
        Assert(structuralStabilityEffect < persistenceFullRangeEffect, $"Case E: StructuralStability's effect (fixed Persistence) must be smaller than Persistence's own full-range effect - observed SS={structuralStabilityEffect:F4}, Persistence={persistenceFullRangeEffect:F4}.");
    }

    // CASE F: Persistence divergence (0.10 vs 0.90) tested at TWO shared-dimension levels (moderate 0.55,
    // extreme 0.90). Because MeanRevertingRule/StableRangeRule blend every term as a WEIGHTED SUM (never a
    // ratio), Persistence's absolute contribution to ScoreDifference should be INDEPENDENT of how extreme
    // the shared dimensions are - not diluted at high shared-dimension levels. This is a direct mathematical
    // consequence of the linear formula (brief §2 "ne pas modifier les formules... observer"), verified here
    // rather than assumed.
    private static void CaseF_DivergentPersistence_SeparationIndependentOfSharedDimensionLevel()
    {
        double deltaModerate =
            ScoreDifference(BuildFusion(0.55, 0.10, 0.55, 0.30, 0.65)) - ScoreDifference(BuildFusion(0.55, 0.90, 0.55, 0.30, 0.65));
        double deltaExtreme =
            ScoreDifference(BuildFusion(0.90, 0.10, 0.90, 0.30, 0.65)) - ScoreDifference(BuildFusion(0.90, 0.90, 0.90, 0.30, 0.65));

        Assert(
            Math.Abs(deltaModerate - deltaExtreme) < 0.01,
            $"Case F: the Persistence-driven separation (0.10 vs 0.90) must be nearly identical whether the shared dimensions are moderate or extreme (additive formula, no dilution) - observed moderate={deltaModerate:F4}, extreme={deltaExtreme:F4}.");
    }

    private static double ScoreDifference(FusionResult fusion)
    {
        DecisionResult result = Evaluate(fusion);
        double mr = result.Candidates.First(c => c.MarketState == MarketState.MeanReverting).FinalScore;
        double sr = result.Candidates.First(c => c.MarketState == MarketState.StableRange).FinalScore;
        return mr - sr;
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

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
