using System;
using System.Collections.Generic;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Entry;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.ScientificFusion;
using IQIAIndicator.Engine.ScientificModels.Abstractions;

namespace IQIAIndicator.Tests.EntryTrigger;

/// <summary>
/// Sprint 15.5 (C1). Direct, unit-level coverage of EntryTriggerBuilder.DetermineDirection's new
/// coherence gate (Sprint 15.4 audit finding BC-03: Direction was previously computed from the raw
/// DynamicZScore sign alone, with no access to DecisionResult at all). Builds EntryTriggerContext by
/// hand with a controlled DynamicZScore and a real DecisionResult/MethodologySelection (via the real
/// MethodologyEngine, not a hand-rolled substitute), so each gate condition (missing decision,
/// non-MeanReverting winner, ambiguous decision, watchlist override) can be exercised in isolation and
/// deterministically - the full SignalEngine pipeline cannot guarantee a specific DynamicZScore sign
/// without reverse-engineering Kalman filter convergence, which this sprint must not need to do.
/// </summary>
public static class DecisionDirectionCoherenceTests
{
    public static void RunAll()
    {
        AssertDecisionResultPropagatesThroughMethodologySelectionIntoEntryTriggerContext();
        AssertMeanRevertingLowAmbiguityNegativeZScoreProducesBuy();
        AssertMeanRevertingLowAmbiguityPositiveZScoreProducesSell();
        AssertMissingDynamicZScoreProducesNoAction();
        AssertAmbiguousMeanRevertingDecisionProducesNoActionEvenWithAValidZScore();
        AssertNonMeanRevertingWinnerNeverProducesDirectionEvenWithAValidZScore();
        AssertMissingDecisionContextProducesNoAction();
        AssertWatchlistStatusStillReturnsWatchRegardlessOfDecision();
        AssertSuppressionReasonIsRecordedForAmbiguousDecision();
        AssertSuppressionReasonIsRecordedForUnsupportedRegime();

        // Sprint 15.7.1 — NO_ACTION diagnostic transparency (observability only, no trading behaviour
        // change: TEST 1-6 from the sprint spec).
        AssertZeroDynamicZScoreProducesNoActionWithPriceAtEquilibriumReason();
        AssertNegativeDynamicZScoreStillProducesBuyWithReadyReason();
        AssertPositiveDynamicZScoreStillProducesSellWithReadyReason();
        AssertAmbiguousDecisionProducesNoActionWithDecisionAmbiguousReason();
        AssertMissingDynamicZScoreProducesNoActionWithDynamicZScoreUnavailableReason();
        AssertSprint157ScenarioBAndCStillProduceBuyCandidate();
    }

    // ── Decision → Entry propagation ────────────────────────────────────────────────────────────

    private static void AssertDecisionResultPropagatesThroughMethodologySelectionIntoEntryTriggerContext()
    {
        var decisionResult = new DecisionResult
        {
            Winner = MarketState.MeanReverting,
            Confidence = 0.83,
            AmbiguityScore = 0.21
        };

        MethodologySelection methodologySelection = new MethodologyEngine().Evaluate(decisionResult);
        EntryTriggerContext context = BuildContext(methodologySelection, dynamicZScore: null, OpportunityStatus.QUALIFIED);

        DecisionResult? propagated = context.MethodologySelection?.DecisionResult;
        Assert(propagated is not null, "DecisionResult must survive DecisionResult -> MethodologySelection -> EntryTriggerContext.");
        Assert(propagated!.Winner == MarketState.MeanReverting, $"Winner must survive propagation unchanged. Actual={propagated.Winner}.");
        Assert(propagated.Confidence == 0.83, $"Confidence must survive propagation unchanged. Actual={propagated.Confidence}.");
        Assert(propagated.AmbiguityScore == 0.21, $"AmbiguityScore must survive propagation unchanged. Actual={propagated.AmbiguityScore}.");
    }

    // ── Direction ────────────────────────────────────────────────────────────────────────────────

    private static void AssertMeanRevertingLowAmbiguityNegativeZScoreProducesBuy()
    {
        EntryTriggerCandidate candidate = Trigger(MarketState.MeanReverting, ambiguityScore: 0.1, dynamicZScore: -2.0);
        Assert(candidate.Assessment.Direction == DirectionCandidate.BUY_CANDIDATE,
            $"MeanReverting winner, low ambiguity, negative DynamicZScore must produce BUY_CANDIDATE. Actual={candidate.Assessment.Direction}.");
    }

    private static void AssertMeanRevertingLowAmbiguityPositiveZScoreProducesSell()
    {
        EntryTriggerCandidate candidate = Trigger(MarketState.MeanReverting, ambiguityScore: 0.1, dynamicZScore: 2.0);
        Assert(candidate.Assessment.Direction == DirectionCandidate.SELL_CANDIDATE,
            $"MeanReverting winner, low ambiguity, positive DynamicZScore must produce SELL_CANDIDATE. Actual={candidate.Assessment.Direction}.");
    }

    private static void AssertMissingDynamicZScoreProducesNoAction()
    {
        EntryTriggerCandidate candidate = Trigger(MarketState.MeanReverting, ambiguityScore: 0.1, dynamicZScore: null);
        Assert(candidate.Assessment.Direction == DirectionCandidate.NO_ACTION,
            $"MeanReverting winner with no available DynamicZScore must produce NO_ACTION, never a fabricated direction. Actual={candidate.Assessment.Direction}.");
    }

    private static void AssertAmbiguousMeanRevertingDecisionProducesNoActionEvenWithAValidZScore()
    {
        EntryTriggerCandidate candidate = Trigger(MarketState.MeanReverting, ambiguityScore: 0.7, dynamicZScore: -2.0);
        Assert(candidate.Assessment.Direction == DirectionCandidate.NO_ACTION,
            $"An ambiguous decision (AmbiguityScore>=0.5) must suppress Direction even when the DynamicZScore sign is clear. Actual={candidate.Assessment.Direction}.");
    }

    private static void AssertNonMeanRevertingWinnerNeverProducesDirectionEvenWithAValidZScore()
    {
        EntryTriggerCandidate candidate = Trigger(MarketState.Trending, ambiguityScore: 0.0, dynamicZScore: -2.0);
        Assert(candidate.Assessment.Direction == DirectionCandidate.NO_ACTION,
            $"A non-MeanReverting winner must never produce BUY/SELL, regardless of any raw metric - no other regime has a model capable of a directional read. Actual={candidate.Assessment.Direction}.");
    }

    private static void AssertMissingDecisionContextProducesNoAction()
    {
        EntryTriggerContext context = BuildContext(methodologySelection: null, dynamicZScore: -2.0, OpportunityStatus.QUALIFIED);
        var candidate = new EntryTriggerBuilder().Build(context);
        Assert(candidate.Assessment.Direction == DirectionCandidate.NO_ACTION,
            $"With no DecisionResult available at all, Direction must fail safe to NO_ACTION rather than trust an unverifiable z-score. Actual={candidate.Assessment.Direction}.");
    }

    private static void AssertWatchlistStatusStillReturnsWatchRegardlessOfDecision()
    {
        EntryTriggerCandidate candidate = Trigger(MarketState.Trending, ambiguityScore: 0.0, dynamicZScore: -2.0, OpportunityStatus.WATCHLIST);
        Assert(candidate.Assessment.Direction == DirectionCandidate.WATCH,
            $"WATCHLIST opportunity status must still short-circuit to WATCH, unaffected by the new decision-coherence gate. Actual={candidate.Assessment.Direction}.");
    }

    private static void AssertSuppressionReasonIsRecordedForAmbiguousDecision()
    {
        EntryTriggerCandidate candidate = Trigger(MarketState.MeanReverting, ambiguityScore: 0.9, dynamicZScore: -2.0);
        bool hasReason = ContainsSubstring(candidate.Diagnostics, "ambiguity");
        Assert(hasReason, "Suppressing Direction for an ambiguous decision must leave a traceable diagnostic, not a silent NO_ACTION.");
    }

    private static void AssertSuppressionReasonIsRecordedForUnsupportedRegime()
    {
        EntryTriggerCandidate candidate = Trigger(MarketState.RandomWalk, ambiguityScore: 0.0, dynamicZScore: -2.0);
        bool hasReason = ContainsSubstring(candidate.Diagnostics, "RandomWalk");
        Assert(hasReason, "Suppressing Direction for an unsupported regime must name the regime in a traceable diagnostic.");
    }

    // ── Sprint 15.7.1: NO_ACTION diagnostic transparency ────────────────────────────────────────────

    // TEST 1: Z = 0 must remain NO_ACTION (never fabricated into BUY/SELL) but must now name the
    // reason instead of silently falling through to the generic READY reason.
    private static void AssertZeroDynamicZScoreProducesNoActionWithPriceAtEquilibriumReason()
    {
        EntryTriggerCandidate candidate = Trigger(MarketState.MeanReverting, ambiguityScore: 0.1, dynamicZScore: 0.0);
        Assert(candidate.Assessment.Direction == DirectionCandidate.NO_ACTION,
            $"DynamicZScore == 0 must still produce NO_ACTION, never a fabricated BUY/SELL. Actual={candidate.Assessment.Direction}.");
        Assert(candidate.Assessment.Reason == EntryTriggerReason.PRICE_AT_EQUILIBRIUM,
            $"DynamicZScore == 0 with TriggerStatus READY must report Reason=PRICE_AT_EQUILIBRIUM. Actual={candidate.Assessment.Reason}.");
    }

    // TEST 2: negative z-score behaviour and its READY reason must be unchanged.
    private static void AssertNegativeDynamicZScoreStillProducesBuyWithReadyReason()
    {
        EntryTriggerCandidate candidate = Trigger(MarketState.MeanReverting, ambiguityScore: 0.1, dynamicZScore: -2.0);
        Assert(candidate.Assessment.Direction == DirectionCandidate.BUY_CANDIDATE,
            $"Negative DynamicZScore must still produce BUY_CANDIDATE, unchanged by Sprint 15.7.1. Actual={candidate.Assessment.Direction}.");
        Assert(candidate.Assessment.Reason == EntryTriggerReason.READY,
            $"BUY_CANDIDATE with TriggerStatus READY must keep Reason=READY, unchanged by Sprint 15.7.1. Actual={candidate.Assessment.Reason}.");
    }

    // TEST 3: positive z-score behaviour and its READY reason must be unchanged.
    private static void AssertPositiveDynamicZScoreStillProducesSellWithReadyReason()
    {
        EntryTriggerCandidate candidate = Trigger(MarketState.MeanReverting, ambiguityScore: 0.1, dynamicZScore: 2.0);
        Assert(candidate.Assessment.Direction == DirectionCandidate.SELL_CANDIDATE,
            $"Positive DynamicZScore must still produce SELL_CANDIDATE, unchanged by Sprint 15.7.1. Actual={candidate.Assessment.Direction}.");
        Assert(candidate.Assessment.Reason == EntryTriggerReason.READY,
            $"SELL_CANDIDATE with TriggerStatus READY must keep Reason=READY, unchanged by Sprint 15.7.1. Actual={candidate.Assessment.Reason}.");
    }

    // TEST 4: an ambiguous decision must keep suppressing Direction (unchanged) and now reports the
    // existing ambiguity diagnostic through the structured Reason field too.
    private static void AssertAmbiguousDecisionProducesNoActionWithDecisionAmbiguousReason()
    {
        EntryTriggerCandidate candidate = Trigger(MarketState.MeanReverting, ambiguityScore: 0.8, dynamicZScore: -2.0);
        Assert(candidate.Assessment.Direction == DirectionCandidate.NO_ACTION,
            $"AmbiguityScore >= 0.5 must still suppress Direction to NO_ACTION, unchanged by Sprint 15.7.1. Actual={candidate.Assessment.Direction}.");
        Assert(candidate.Assessment.Reason == EntryTriggerReason.DECISION_AMBIGUOUS,
            $"Ambiguous decision with TriggerStatus READY must report Reason=DECISION_AMBIGUOUS. Actual={candidate.Assessment.Reason}.");
    }

    // TEST 5: DynamicZScore unavailable must keep suppressing Direction (unchanged) and now reports it
    // through the structured Reason field too.
    private static void AssertMissingDynamicZScoreProducesNoActionWithDynamicZScoreUnavailableReason()
    {
        EntryTriggerCandidate candidate = Trigger(MarketState.MeanReverting, ambiguityScore: 0.1, dynamicZScore: null);
        Assert(candidate.Assessment.Direction == DirectionCandidate.NO_ACTION,
            $"Missing DynamicZScore must still produce NO_ACTION, unchanged by Sprint 15.7.1. Actual={candidate.Assessment.Direction}.");
        Assert(candidate.Assessment.Reason == EntryTriggerReason.DYNAMIC_ZSCORE_UNAVAILABLE,
            $"Missing DynamicZScore with TriggerStatus READY must report Reason=DYNAMIC_ZSCORE_UNAVAILABLE. Actual={candidate.Assessment.Reason}.");
    }

    // TEST 6 (non-regression): the exact DynamicZScore values from Sprint 15.7's Scenario B and C
    // (weak and normal oscillation) must keep producing BUY_CANDIDATE.
    private static void AssertSprint157ScenarioBAndCStillProduceBuyCandidate()
    {
        EntryTriggerCandidate scenarioB = Trigger(MarketState.MeanReverting, ambiguityScore: 0.1, dynamicZScore: -0.1018);
        Assert(scenarioB.Assessment.Direction == DirectionCandidate.BUY_CANDIDATE,
            $"Sprint 15.7 Scenario B (DynamicZScore=-0.1018) must still produce BUY_CANDIDATE. Actual={scenarioB.Assessment.Direction}.");

        EntryTriggerCandidate scenarioC = Trigger(MarketState.MeanReverting, ambiguityScore: 0.1, dynamicZScore: -0.7266);
        Assert(scenarioC.Assessment.Direction == DirectionCandidate.BUY_CANDIDATE,
            $"Sprint 15.7 Scenario C (DynamicZScore=-0.7266) must still produce BUY_CANDIDATE. Actual={scenarioC.Assessment.Direction}.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static EntryTriggerCandidate Trigger(
        MarketState winner,
        double ambiguityScore,
        double? dynamicZScore,
        OpportunityStatus opportunityStatus = OpportunityStatus.QUALIFIED)
    {
        var decisionResult = new DecisionResult { Winner = winner, Confidence = 0.9, AmbiguityScore = ambiguityScore };
        MethodologySelection methodologySelection = new MethodologyEngine().Evaluate(decisionResult);
        EntryTriggerContext context = BuildContext(methodologySelection, dynamicZScore, opportunityStatus);
        return new EntryTriggerBuilder().Build(context);
    }

    private static EntryTriggerContext BuildContext(
        MethodologySelection? methodologySelection,
        double? dynamicZScore,
        OpportunityStatus opportunityStatus)
    {
        var scientificAssessment = new ScientificAssessment(
            OverallConfidence: 0.8,
            EvidenceAgreement: Array.Empty<string>(),
            EvidenceConflict: Array.Empty<string>(),
            MissingEvidence: Array.Empty<string>(),
            ExecutedModels: Array.Empty<string>(),
            SuccessfulModels: Array.Empty<string>(),
            FailedModels: Array.Empty<string>(),
            ScientificResults: Array.Empty<ScientificModelResult>(),
            Diagnostics: "test fixture");

        var entryAssessment = new EntryAssessment(
            scientificAssessment,
            AssessmentQuality: 0.8,
            EntryReadiness: EntryReadiness.READY_FOR_NEXT_STAGE,
            OpportunityStatus: opportunityStatus,
            OpportunityPriority: 0.8,
            OpportunityReasons: Array.Empty<string>(),
            BlockingIssues: Array.Empty<string>(),
            SupportingEvidence: Array.Empty<string>(),
            Warnings: Array.Empty<string>(),
            Diagnostics: Array.Empty<string>());

        var entryCandidate = new EntryCandidate(
            entryAssessment,
            DateTime.UtcNow,
            opportunityStatus,
            OpportunityPriority: 0.8,
            OpportunityReasons: Array.Empty<string>(),
            Warnings: Array.Empty<string>(),
            Diagnostics: Array.Empty<string>());

        var businessContext = new EntryBusinessContext(
            CurrentPrice: 100m,
            EstimatedEquilibrium: 100.0,
            DistanceToEquilibrium: 0.0,
            DynamicZScore: dynamicZScore,
            ScientificConfidence: 0.8,
            OpportunityPriority: 0.8,
            Methodology: methodologySelection?.SelectedMethodology.Name,
            Timestamp: DateTime.UtcNow,
            SupportingEvidence: Array.Empty<string>(),
            BlockingIssues: Array.Empty<string>(),
            Diagnostics: Array.Empty<string>(),
            Warnings: Array.Empty<string>(),
            OpportunityReasons: Array.Empty<string>(),
            OpportunityStatus: opportunityStatus);

        return new EntryTriggerContext(businessContext, scientificAssessment, entryAssessment, entryCandidate, methodologySelection);
    }

    private static bool ContainsSubstring(IReadOnlyList<string> values, string fragment)
    {
        foreach (string value in values)
        {
            if (value.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
