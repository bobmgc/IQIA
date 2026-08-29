using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Entry;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.ScientificFusion;
using IQIAIndicator.Engine.Signal;

namespace IQIAIndicator.Tests.Signal;

using ScientificMarketContext = global::IQIAIndicator.Engine.ScientificModels.Abstractions.MarketContext;

/// <summary>
/// Sprint 14 / Part E. A minimal end-to-end integration test through the real, wired pipeline
/// stages - Decision -&gt; Methodology -&gt; ScientificModelRegistry -&gt; scientific models -&gt;
/// ScientificFusion -&gt; Entry -&gt; EntryTrigger - using the actual production components
/// (MethodologyEngine, MethodologyRegistry, ScientificModelRegistry, SignalEngine), not hand-built
/// substitutes. The audit report that commissioned this sprint noted specifically that DEC-01 escaped
/// detection because every existing test exercised one pipeline layer at a time with synthetic
/// hand-built inputs; this file exists to close that particular gap for the coverage-status fix.
/// </summary>
public static class SignalEngineCoverageIntegrationTests
{
    public static void RunAll()
    {
        AssertNoCoverageRegimeNeverFabricatesADirectionalSignal();
        AssertMeanReversionRegimeKeepsTheNormalScientificPath();
        AssertAllSixRegimesMatchTheAuditedCoverageMatrix();
    }

    /// <summary>
    /// Decision selects a regime (Trending) for which ScientificModelRegistry.Resolve returns zero
    /// models (see audit finding ARC-003). Verifies that: (1) no scientific model is executed as if
    /// it existed, (2) the NO MODEL COVERAGE state survives into the ScientificAssessment consumed by
    /// Entry, (3) Entry does not turn this structural gap into a qualified opportunity, and (4)
    /// EntryTrigger does not fabricate a directional (BUY/SELL) signal from it.
    /// </summary>
    private static void AssertNoCoverageRegimeNeverFabricatesADirectionalSignal()
    {
        var decisionResult = new DecisionResult { Winner = MarketState.Trending, Confidence = 0.82 };
        MethodologySelection methodologySelection = new MethodologyEngine().Evaluate(decisionResult);
        Assert(methodologySelection.SelectedMethodology.Name == "TrendFollowingMethodology",
            "Sanity check on the real MethodologyRegistry wiring this test depends on.");

        var marketContext = new ScientificMarketContext(
            DateTime.UtcNow,
            100m,
            BuildHistory(),
            CurrentPrice: 100m);

        var signalEngine = new SignalEngine();
        signalEngine.Process(marketContext, methodologySelection);

        ScientificAssessment? assessment = signalEngine.LastScientificAssessment;
        if (assessment is null)
            throw new InvalidOperationException("LastScientificAssessment must be populated after Process.");

        Assert(assessment.CoverageStatus == ScientificCoverageStatus.NoModelCoverage,
            "A regime with zero registered models must report NoModelCoverage, not be indistinguishable from insufficient-data.");
        Assert(assessment.ExecutedModels.Count == 0, "No model may be executed as if it existed for a regime with no registered models.");
        Assert(assessment.ScientificResults.Count == 0, "No scientific result may be fabricated for a regime with no registered models.");
        Assert(assessment.Diagnostics.Contains("No scientific model coverage", StringComparison.Ordinal),
            "The honest coverage diagnostic must reach the assessment consumed downstream by Entry.");

        EntryCandidate? entryCandidate = signalEngine.LastEntryCandidate;
        if (entryCandidate is null)
            throw new InvalidOperationException("LastEntryCandidate must be populated after Process.");

        Assert(entryCandidate.OpportunityStatus == OpportunityStatus.NOT_QUALIFIED,
            "Entry must not upgrade a structural no-coverage gap into a qualified or watchlisted opportunity.");
        Assert(entryCandidate.Assessment.EntryReadiness == EntryReadiness.INSUFFICIENT_EVIDENCE,
            "Entry readiness must reflect that no evidence exists, not that evidence was merely weak.");

        EntryTriggerCandidate? triggerCandidate = signalEngine.LastEntryTriggerCandidate;
        if (triggerCandidate is null)
            throw new InvalidOperationException("LastEntryTriggerCandidate must be populated after Process.");

        Assert(triggerCandidate.Assessment.Direction == DirectionCandidate.NO_ACTION,
            "EntryTrigger must never turn a no-coverage regime into a BUY/SELL/WATCH directional candidate.");
    }

    /// <summary>
    /// Decision selects MeanReverting, for which ScientificModelRegistry.Resolve returns the full
    /// 5-model stack. Verifies the normal scientific path is unaffected by the coverage-status
    /// addition: all 5 models are genuinely executed and CoverageStatus correctly reports
    /// ModelsExecuted, not NoModelCoverage.
    /// </summary>
    private static void AssertMeanReversionRegimeKeepsTheNormalScientificPath()
    {
        var decisionResult = new DecisionResult { Winner = MarketState.MeanReverting, Confidence = 0.9 };
        MethodologySelection methodologySelection = new MethodologyEngine().Evaluate(decisionResult);
        Assert(methodologySelection.SelectedMethodology.Name == "MeanReversionMethodology",
            "Sanity check on the real MethodologyRegistry wiring this test depends on.");

        var marketContext = new ScientificMarketContext(
            DateTime.UtcNow,
            100m,
            BuildHistory(),
            CurrentPrice: 100m);

        var signalEngine = new SignalEngine();
        signalEngine.Process(marketContext, methodologySelection);

        ScientificAssessment? assessment = signalEngine.LastScientificAssessment;
        if (assessment is null)
            throw new InvalidOperationException("LastScientificAssessment must be populated after Process.");

        Assert(assessment.CoverageStatus == ScientificCoverageStatus.ModelsExecuted,
            "A regime with registered models must report ModelsExecuted, never NoModelCoverage.");
        Assert(assessment.ExecutedModels.Count == 5,
            "All five Mean-Reversion models (Kalman, Ornstein-Uhlenbeck, Dynamic Z-Score, Volatility, SPRT) must genuinely execute via the real ScientificModelRegistry.");
        Assert(assessment.ScientificResults.Count == 5, "Each executed model must produce a real ScientificModelResult.");
        Assert(!assessment.Diagnostics.Contains("No scientific model coverage", StringComparison.Ordinal),
            "The no-coverage diagnostic must never appear when models were actually registered and executed.");
    }

    /// <summary>
    /// Sprint 15.5 (C1/C2/C3). Full-pipeline confirmation of the 6-regime coverage matrix established
    /// by the Sprint 15.4 audit and fixed by this sprint: exactly one regime (MeanReverting) has real
    /// scientific model coverage, and Direction is never fabricated for any of the other five -
    /// through the real DecisionEngine -&gt; MethodologyEngine -&gt; ScientificModelRegistry -&gt; SignalEngine
    /// wiring, not a hand-built substitute. See MethodologyRegistryCoverageTests (Tests/Methodology)
    /// for the equivalent unit-level matrix and DecisionDirectionCoherenceTests
    /// (Tests/EntryTrigger) for deterministic BUY/SELL coverage.
    /// </summary>
    private static void AssertAllSixRegimesMatchTheAuditedCoverageMatrix()
    {
        (MarketState State, string ExpectedMethodology, int ExpectedModelCount, bool DirectionMustBeNoAction)[] matrix =
        [
            (MarketState.MeanReverting, "MeanReversionMethodology", 5, false),
            (MarketState.Trending, "TrendFollowingMethodology", 0, true),
            (MarketState.StructuralBreak, "StructuralBreakMethodology", 0, true),
            (MarketState.RandomWalk, "RandomWalkMethodology", 0, true),
            (MarketState.StableRange, "StableRangeMethodology", 0, true),
            (MarketState.Transitional, "TransitionalMethodology", 0, true),
        ];

        foreach ((MarketState state, string expectedMethodology, int expectedModelCount, bool directionMustBeNoAction) in matrix)
        {
            var decisionResult = new DecisionResult { Winner = state, Confidence = 0.85, AmbiguityScore = 0.1 };
            MethodologySelection methodologySelection = new MethodologyEngine().Evaluate(decisionResult);
            Assert(methodologySelection.SelectedMethodology.Name == expectedMethodology,
                $"{state}: expected methodology={expectedMethodology}, actual={methodologySelection.SelectedMethodology.Name}.");

            var marketContext = new ScientificMarketContext(DateTime.UtcNow, 100m, BuildHistory(), CurrentPrice: 100m);
            var signalEngine = new SignalEngine();
            signalEngine.Process(marketContext, methodologySelection);

            ScientificAssessment? assessment = signalEngine.LastScientificAssessment;
            if (assessment is null)
                throw new InvalidOperationException($"{state}: LastScientificAssessment must be populated after Process.");

            Assert(assessment.ExecutedModels.Count == expectedModelCount,
                $"{state}: expected {expectedModelCount} executed models, actual={assessment.ExecutedModels.Count}.");
            Assert(assessment.CoverageStatus == (expectedModelCount == 0 ? ScientificCoverageStatus.NoModelCoverage : ScientificCoverageStatus.ModelsExecuted),
                $"{state}: CoverageStatus mismatch. Actual={assessment.CoverageStatus}.");

            EntryTriggerCandidate? triggerCandidate = signalEngine.LastEntryTriggerCandidate;
            if (triggerCandidate is null)
                throw new InvalidOperationException($"{state}: LastEntryTriggerCandidate must be populated after Process.");

            if (directionMustBeNoAction)
            {
                Assert(triggerCandidate.Assessment.Direction == DirectionCandidate.NO_ACTION,
                    $"{state}: a regime with no directionally-capable model must never produce BUY/SELL/WATCH. Actual={triggerCandidate.Assessment.Direction}.");
            }
        }
    }

    private static IReadOnlyList<decimal> BuildHistory()
    {
        var history = new List<decimal>();
        decimal[] pattern = { 100m, 101m, 99m, 100.5m, 99.5m, 100m, 100.8m, 99.2m };
        for (int i = 0; i < 6; i++)
        {
            history.AddRange(pattern);
        }

        return history;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
