using System.Linq;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Engine.ScientificModels.Registry;

namespace IQIAIndicator.Tests.Methodology;

/// <summary>
/// Sprint 15.5 (C2/C3). Real-code matrix of the six reachable <see cref="MarketState"/> values (plus
/// Unknown) against what <see cref="MethodologyRegistry"/> and <see cref="ScientificModelRegistry"/>
/// actually produce - built from direct inspection of both classes and
/// Engine/ScientificModels (Sprint 15.4 audit + Sprint 15.5 fix), not assumed:
///
/// | Regime         | Methodology                | Real models wired | Direction possible | Status      |
/// |----------------|-----------------------------|--------------------|---------------------|-------------|
/// | MeanReverting  | MeanReversionMethodology    | 5 (Kalman, OU, DynamicZScore, Volatility, SPRT) | Yes (DynamicZScore sign) | SUPPORTED   |
/// | Trending       | TrendFollowingMethodology   | 0 (PrimaryModel + BOCPD are placeholder stubs)  | No | UNSUPPORTED |
/// | StructuralBreak| StructuralBreakMethodology  | 0 (PrimaryModel BOCPD is a placeholder stub)    | No | UNSUPPORTED |
/// | RandomWalk     | RandomWalkMethodology       | 0 (no model class exists for it)                | No | UNSUPPORTED |
/// | StableRange    | StableRangeMethodology      | 0 (no methodology-specific model exists)        | No | UNSUPPORTED (explicit, distinct from Unknown) |
/// | Transitional   | TransitionalMethodology     | 0 (no methodology-specific model exists)        | No | UNSUPPORTED (explicit, distinct from Unknown) |
/// | Unknown        | UnknownMethodology          | 0                                                | No | UNKNOWN (genuinely unrecognized, not "known but unsupported") |
/// </summary>
public static class MethodologyRegistryCoverageTests
{
    public static void RunAll()
    {
        AssertMeanRevertingIsTheOnlySupportedRegime();
        AssertTrendingIsExplicitlyUnsupported();
        AssertStructuralBreakIsExplicitlyUnsupported();
        AssertRandomWalkIsExplicitlyUnsupported();
        AssertStableRangeIsExplicitlyUnsupportedAndDistinctFromUnknown();
        AssertTransitionalIsExplicitlyUnsupportedAndDistinctFromUnknown();
        AssertUnknownRegimeIsNotConfusedWithAKnownButUnsupportedRegime();
        AssertFullSixRegimeMatrix();
    }

    private static void AssertMeanRevertingIsTheOnlySupportedRegime()
    {
        (QuantitativeMethodology methodology, var models) = Resolve(MarketState.MeanReverting);

        Assert(methodology.Name == "MeanReversionMethodology", "MeanReverting must resolve to MeanReversionMethodology.");
        Assert(models.Count == 5, $"MeanReversionMethodology must wire exactly its 5 genuine models. Actual={models.Count}.");
        Assert(models.Select(model => model.Name).Contains("DynamicZScoreModel"),
            "DynamicZScoreModel - the only model capable of a directional read - must be part of the MeanReverting bundle.");
    }

    private static void AssertTrendingIsExplicitlyUnsupported()
    {
        (QuantitativeMethodology methodology, var models) = Resolve(MarketState.Trending);

        Assert(methodology.Name == "TrendFollowingMethodology", "Trending must resolve to TrendFollowingMethodology.");
        Assert(models.Count == 0,
            "TrendFollowingMethodology must resolve to zero models: its PrimaryModel (Time Series Momentum) and BOCPD supporting model are unimplemented placeholder stubs - wiring them in would fabricate coverage.");
    }

    private static void AssertStructuralBreakIsExplicitlyUnsupported()
    {
        (QuantitativeMethodology methodology, var models) = Resolve(MarketState.StructuralBreak);

        Assert(methodology.Name == "StructuralBreakMethodology", "StructuralBreak must resolve to StructuralBreakMethodology.");
        Assert(models.Count == 0,
            "StructuralBreakMethodology must resolve to zero models: its PrimaryModel (BOCPD) is an unimplemented placeholder stub.");
    }

    private static void AssertRandomWalkIsExplicitlyUnsupported()
    {
        (QuantitativeMethodology methodology, var models) = Resolve(MarketState.RandomWalk);

        Assert(methodology.Name == "RandomWalkMethodology", "RandomWalk must resolve to RandomWalkMethodology.");
        Assert(models.Count == 0,
            "RandomWalkMethodology must resolve to zero models: no 'Random Walk Null Model' class exists, and it declares zero SupportingModels.");
    }

    private static void AssertStableRangeIsExplicitlyUnsupportedAndDistinctFromUnknown()
    {
        (QuantitativeMethodology methodology, var models) = Resolve(MarketState.StableRange);

        Assert(methodology.Name == "StableRangeMethodology",
            "StableRange must resolve to its own explicitly-named methodology, not silently share UnknownMethodology's bucket (Sprint 15.4 finding BC-04).");
        Assert(methodology.Name != "UnknownMethodology", "StableRange must never be reported as UnknownMethodology.");
        Assert(models.Count == 0, "StableRangeMethodology must resolve to zero models - no methodology-specific model is implemented yet.");
    }

    private static void AssertTransitionalIsExplicitlyUnsupportedAndDistinctFromUnknown()
    {
        (QuantitativeMethodology methodology, var models) = Resolve(MarketState.Transitional);

        Assert(methodology.Name == "TransitionalMethodology",
            "Transitional must resolve to its own explicitly-named methodology, not silently share UnknownMethodology's bucket (Sprint 15.4 finding BC-04).");
        Assert(methodology.Name != "UnknownMethodology", "Transitional must never be reported as UnknownMethodology.");
        Assert(models.Count == 0, "TransitionalMethodology must resolve to zero models - no methodology-specific model is implemented yet.");
    }

    /// <summary>
    /// The specific regression this sprint must prevent: a genuinely unrecognized/invalid regime must
    /// stay distinguishable from a regime that IS recognized (has its own named methodology) but is not
    /// yet scientifically supported. Both currently produce zero models, but only Unknown may report
    /// "UnknownMethodology" - StableRange/Transitional must not, even though all three behave
    /// identically downstream today.
    /// </summary>
    private static void AssertUnknownRegimeIsNotConfusedWithAKnownButUnsupportedRegime()
    {
        (QuantitativeMethodology unknownMethodology, var unknownModels) = Resolve(MarketState.Unknown);
        (QuantitativeMethodology stableRangeMethodology, _) = Resolve(MarketState.StableRange);
        (QuantitativeMethodology transitionalMethodology, _) = Resolve(MarketState.Transitional);

        Assert(unknownMethodology.Name == "UnknownMethodology", "MarketState.Unknown must resolve to UnknownMethodology.");
        Assert(unknownModels.Count == 0, "UnknownMethodology must resolve to zero models.");
        Assert(unknownMethodology.Name != stableRangeMethodology.Name,
            "Unknown and StableRange must resolve to different methodology names, even though both are currently unsupported.");
        Assert(unknownMethodology.Name != transitionalMethodology.Name,
            "Unknown and Transitional must resolve to different methodology names, even though both are currently unsupported.");
    }

    /// <summary>Full matrix in one pass: every one of the 6 reachable regimes, cross-checked against the table in this file's own doc comment.</summary>
    private static void AssertFullSixRegimeMatrix()
    {
        (MarketState State, string ExpectedMethodology, int ExpectedModelCount)[] matrix =
        [
            (MarketState.MeanReverting, "MeanReversionMethodology", 5),
            (MarketState.Trending, "TrendFollowingMethodology", 0),
            (MarketState.StructuralBreak, "StructuralBreakMethodology", 0),
            (MarketState.RandomWalk, "RandomWalkMethodology", 0),
            (MarketState.StableRange, "StableRangeMethodology", 0),
            (MarketState.Transitional, "TransitionalMethodology", 0),
        ];

        foreach ((MarketState state, string expectedMethodology, int expectedModelCount) in matrix)
        {
            (QuantitativeMethodology methodology, var models) = Resolve(state);
            Assert(methodology.Name == expectedMethodology, $"{state}: expected methodology={expectedMethodology}, actual={methodology.Name}.");
            Assert(models.Count == expectedModelCount, $"{state}: expected model count={expectedModelCount}, actual={models.Count}.");
        }
    }

    private static (QuantitativeMethodology Methodology, System.Collections.Generic.IReadOnlyList<IScientificModel> Models) Resolve(MarketState marketState)
    {
        var decisionResult = new DecisionResult { Winner = marketState, Confidence = 0.9, AmbiguityScore = 0.0 };
        MethodologySelection methodologySelection = new MethodologyEngine().Evaluate(decisionResult);
        var models = new ScientificModelRegistry().Resolve(methodologySelection);
        return (methodologySelection.SelectedMethodology, models);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new System.InvalidOperationException(message);
    }
}
