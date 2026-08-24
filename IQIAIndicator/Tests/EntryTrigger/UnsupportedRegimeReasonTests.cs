using System;
using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Core;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Entry;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.ScientificFusion;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Engine.Signal;

namespace IQIAIndicator.Tests.EntryTrigger;

using ScientificMarketContext = global::IQIAIndicator.Engine.ScientificModels.Abstractions.MarketContext;

/// <summary>
/// Sprint 15.25 (Lot 15.1, "Multi-Regime Signal/Entry Foundation Audit &amp; Correction"). Additive
/// coverage for <see cref="EntryTriggerReason.UNSUPPORTED_REGIME"/> (introduced this lot in
/// <see cref="EntryTriggerAssessment"/> and surfaced in <see cref="EntryTriggerBuilder"/> - see that
/// file's own doc comments for the exact, unchanged gating condition
/// <c>decision.Winner != MarketState.MeanReverting</c>).
///
/// Two complementary proofs, matching the pattern already established by
/// <see cref="DecisionDirectionCoherenceTests"/> (hand-built <see cref="EntryTriggerContext"/>, unit-level,
/// deterministic) and <see cref="Signal.SignalEngineCoverageIntegrationTests"/> (real
/// MethodologyEngine -&gt; ScientificModelRegistry -&gt; SignalEngine.Process chain, no hand-built
/// substitute for the scientific layer):
///
/// 1. Hand-built EntryTriggerContext, one per MarketState (all 7 values) plus the WATCHLIST override -
///    proves the new Reason value is computed and surfaced correctly in isolation, and that MeanReverting's
///    BUY/SELL/NO_ACTION behaviour and its three pre-existing Sprint 15.7.1 reasons
///    (DYNAMIC_ZSCORE_UNAVAILABLE / PRICE_AT_EQUILIBRIUM / DECISION_AMBIGUOUS) are unchanged by this lot.
/// 2. Real, unmodified MethodologyEngine -&gt; ScientificModelRegistry -&gt; SignalEngine.Process chain, one
///    run per MarketState - proves EntryCandidate/EntryTriggerCandidate can never reach a directional
///    outcome for a regime with zero registered scientific models (brief §31: "EntryCandidate ne peut pas
///    être créé pour un modèle absent"), and that the new Reason value reaches
///    EntryTriggerAssessment.Reason through the real pipeline, not just the hand-built fixture path.
///
/// Also covers determinism (brief §26) and run isolation (brief §28) for the new code path.
/// </summary>
public static class UnsupportedRegimeReasonTests
{
    public static void RunAll()
    {
        // ── 1. Hand-built EntryTriggerContext, per-regime ──────────────────────────────────────────
        AssertMeanRevertingBuyNeverReportsUnsupportedRegime();
        AssertMeanRevertingSellNeverReportsUnsupportedRegime();
        AssertMeanRevertingNoActionReasonsNeverReportUnsupportedRegime();

        foreach (MarketState regime in UnsupportedRegimes())
        {
            AssertUnsupportedRegimeProducesExplicitNoActionReason(regime);
        }

        AssertWatchlistOverrideStillWorksForUnsupportedRegime();

        // ── 2. Real MethodologyEngine -> ScientificModelRegistry -> SignalEngine.Process chain ─────
        AssertRealPipelineNeverFabricatesDirectionForAnyUnsupportedRegime();
        AssertRealPipelineMeanRevertingReasonIsNeverUnsupportedRegime();

        // ── 3. Determinism / run isolation ──────────────────────────────────────────────────────────
        AssertDeterministicAcrossTwoIndependentBuilds();
        AssertNoStateLeaksBetweenConsecutiveSignalEngineRuns();
    }

    private static IEnumerable<MarketState> UnsupportedRegimes() => new[]
    {
        MarketState.Trending,
        MarketState.RandomWalk,
        MarketState.StructuralBreak,
        MarketState.StableRange,
        MarketState.Unknown,
        MarketState.Transitional
    };

    // ── 1a. MeanReverting regression (brief §25: no behaviour change for the one supported regime) ──

    private static void AssertMeanRevertingBuyNeverReportsUnsupportedRegime()
    {
        EntryTriggerCandidate candidate = Trigger(MarketState.MeanReverting, ambiguityScore: 0.1, dynamicZScore: -2.0);
        Assert(candidate.Assessment.Direction == DirectionCandidate.BUY_CANDIDATE,
            $"MeanReverting, low ambiguity, negative DynamicZScore must still produce BUY_CANDIDATE. Actual={candidate.Assessment.Direction}.");
        Assert(candidate.Assessment.Reason == EntryTriggerReason.READY,
            $"MeanReverting BUY must still report Reason=READY, never UNSUPPORTED_REGIME. Actual={candidate.Assessment.Reason}.");
    }

    private static void AssertMeanRevertingSellNeverReportsUnsupportedRegime()
    {
        EntryTriggerCandidate candidate = Trigger(MarketState.MeanReverting, ambiguityScore: 0.1, dynamicZScore: 2.0);
        Assert(candidate.Assessment.Direction == DirectionCandidate.SELL_CANDIDATE,
            $"MeanReverting, low ambiguity, positive DynamicZScore must still produce SELL_CANDIDATE. Actual={candidate.Assessment.Direction}.");
        Assert(candidate.Assessment.Reason == EntryTriggerReason.READY,
            $"MeanReverting SELL must still report Reason=READY, never UNSUPPORTED_REGIME. Actual={candidate.Assessment.Reason}.");
    }

    private static void AssertMeanRevertingNoActionReasonsNeverReportUnsupportedRegime()
    {
        // DynamicZScore unavailable.
        EntryTriggerCandidate missingZ = Trigger(MarketState.MeanReverting, ambiguityScore: 0.1, dynamicZScore: null);
        Assert(missingZ.Assessment.Direction == DirectionCandidate.NO_ACTION, "Missing DynamicZScore must still be NO_ACTION.");
        Assert(missingZ.Assessment.Reason == EntryTriggerReason.DYNAMIC_ZSCORE_UNAVAILABLE,
            $"Missing DynamicZScore on MeanReverting must still report DYNAMIC_ZSCORE_UNAVAILABLE, never UNSUPPORTED_REGIME. Actual={missingZ.Assessment.Reason}.");

        // Price at equilibrium.
        EntryTriggerCandidate zeroZ = Trigger(MarketState.MeanReverting, ambiguityScore: 0.1, dynamicZScore: 0.0);
        Assert(zeroZ.Assessment.Direction == DirectionCandidate.NO_ACTION, "Zero DynamicZScore must still be NO_ACTION.");
        Assert(zeroZ.Assessment.Reason == EntryTriggerReason.PRICE_AT_EQUILIBRIUM,
            $"DynamicZScore==0 on MeanReverting must still report PRICE_AT_EQUILIBRIUM, never UNSUPPORTED_REGIME. Actual={zeroZ.Assessment.Reason}.");

        // Ambiguous decision.
        EntryTriggerCandidate ambiguous = Trigger(MarketState.MeanReverting, ambiguityScore: 0.97, dynamicZScore: -2.0);
        Assert(ambiguous.Assessment.Direction == DirectionCandidate.NO_ACTION, "Ambiguous decision must still be NO_ACTION.");
        Assert(ambiguous.Assessment.Reason == EntryTriggerReason.DECISION_AMBIGUOUS,
            $"Ambiguous MeanReverting decision must still report DECISION_AMBIGUOUS, never UNSUPPORTED_REGIME. Actual={ambiguous.Assessment.Reason}.");
    }

    // ── 1b. Every non-MeanReverting regime: explicit, named NO_ACTION reason ──────────────────────────

    private static void AssertUnsupportedRegimeProducesExplicitNoActionReason(MarketState regime)
    {
        // dynamicZScore is intentionally a valid, clearly-directional value (-2.0) and ambiguityScore is
        // intentionally low (0.0) - proves the regime-mismatch gate alone is sufficient to suppress
        // Direction, not merely a side effect of ambiguity or a missing metric (same intent as the
        // pre-existing AssertNonMeanRevertingWinnerNeverProducesDirectionEvenWithAValidZScore in
        // DecisionDirectionCoherenceTests, extended here to also check the new Reason field).
        EntryTriggerCandidate candidate = Trigger(regime, ambiguityScore: 0.0, dynamicZScore: -2.0);

        Assert(candidate.Assessment.Direction == DirectionCandidate.NO_ACTION,
            $"{regime}: a regime with no directionally-capable model must never produce BUY/SELL/WATCH. Actual={candidate.Assessment.Direction}.");
        Assert(candidate.Assessment.Reason == EntryTriggerReason.UNSUPPORTED_REGIME,
            $"{regime}: Reason must be UNSUPPORTED_REGIME. Actual={candidate.Assessment.Reason}.");

        bool namesRegime = ContainsSubstring(candidate.Diagnostics, regime.ToString());
        Assert(namesRegime, $"{regime}: diagnostic must name the regime explicitly. Diagnostics=[{string.Join("; ", candidate.Diagnostics)}].");

        bool mentionsCoverageStatus = ContainsSubstring(candidate.Diagnostics, "CoverageStatus");
        Assert(mentionsCoverageStatus, $"{regime}: diagnostic must mention CoverageStatus. Diagnostics=[{string.Join("; ", candidate.Diagnostics)}].");
    }

    private static void AssertWatchlistOverrideStillWorksForUnsupportedRegime()
    {
        // The WATCHLIST short-circuit in DetermineDirection runs BEFORE the regime-mismatch branch, so
        // it must still take priority over UNSUPPORTED_REGIME for an unsupported regime, exactly as it
        // already does for MeanReverting (AssertWatchlistStatusStillReturnsWatchRegardlessOfDecision in
        // DecisionDirectionCoherenceTests).
        EntryTriggerCandidate candidate = Trigger(MarketState.RandomWalk, ambiguityScore: 0.0, dynamicZScore: -2.0, OpportunityStatus.WATCHLIST);
        Assert(candidate.Assessment.Direction == DirectionCandidate.WATCH,
            $"WATCHLIST must still short-circuit to WATCH even for an unsupported regime. Actual={candidate.Assessment.Direction}.");
        Assert(candidate.Assessment.Reason != EntryTriggerReason.UNSUPPORTED_REGIME,
            $"A WATCH direction must never report UNSUPPORTED_REGIME (that reason only applies when Direction==NO_ACTION). Actual={candidate.Assessment.Reason}.");
    }

    // ── 2. Real pipeline (MethodologyEngine -> ScientificModelRegistry -> SignalEngine.Process) ────────

    /// <summary>
    /// Brief §31 acceptance criterion, end to end: for every regime with zero registered scientific
    /// models, no scientific model is executed, no directional EntryCandidate/EntryTriggerCandidate is
    /// ever produced, and the new UNSUPPORTED_REGIME reason reaches EntryTriggerAssessment.Reason -
    /// through the REAL, unmodified pipeline wiring (MethodologyRegistry + ScientificModelRegistry +
    /// SignalEngine), not a hand-built substitute for the scientific layer.
    /// </summary>
    private static void AssertRealPipelineNeverFabricatesDirectionForAnyUnsupportedRegime()
    {
        foreach (MarketState regime in UnsupportedRegimes())
        {
            var decisionResult = new DecisionResult { Winner = regime, Confidence = 0.85, AmbiguityScore = 0.1 };
            MethodologySelection methodologySelection = new MethodologyEngine().Evaluate(decisionResult);

            var marketContext = new ScientificMarketContext(DateTime.UtcNow, 100m, BuildHistory(), CurrentPrice: 100m);
            var signalEngine = new SignalEngine();
            signalEngine.Process(marketContext, methodologySelection);

            ScientificAssessment? assessment = signalEngine.LastScientificAssessment;
            if (assessment is null)
                throw new InvalidOperationException($"{regime}: LastScientificAssessment must be populated after Process.");

            Assert(assessment.ExecutedModels.Count == 0,
                $"{regime}: no scientific model may be executed for a regime with zero registered models. Actual ExecutedModels.Count={assessment.ExecutedModels.Count}.");
            Assert(assessment.ScientificResults.Count == 0,
                $"{regime}: no scientific result may be fabricated for a regime with zero registered models. Actual={assessment.ScientificResults.Count}.");
            Assert(assessment.CoverageStatus == ScientificCoverageStatus.NoModelCoverage,
                $"{regime}: CoverageStatus must be NoModelCoverage. Actual={assessment.CoverageStatus}.");

            EntryCandidate? entryCandidate = signalEngine.LastEntryCandidate;
            if (entryCandidate is null)
                throw new InvalidOperationException($"{regime}: LastEntryCandidate must be populated after Process.");
            Assert(entryCandidate.OpportunityStatus == OpportunityStatus.NOT_QUALIFIED,
                $"{regime}: EntryCandidate must never be qualified for an absent model (brief §31). Actual={entryCandidate.OpportunityStatus}.");

            EntryTriggerCandidate? triggerCandidate = signalEngine.LastEntryTriggerCandidate;
            if (triggerCandidate is null)
                throw new InvalidOperationException($"{regime}: LastEntryTriggerCandidate must be populated after Process.");

            Assert(triggerCandidate.Assessment.Direction == DirectionCandidate.NO_ACTION,
                $"{regime}: EntryTrigger must never fabricate a directional (BUY/SELL/WATCH) candidate for an absent model. Actual={triggerCandidate.Assessment.Direction}.");
            Assert(triggerCandidate.Assessment.Reason == EntryTriggerReason.UNSUPPORTED_REGIME,
                $"{regime}: through the real pipeline, Reason must reach UNSUPPORTED_REGIME. Actual={triggerCandidate.Assessment.Reason}.");

            bool namesRegime = ContainsSubstring(triggerCandidate.Diagnostics, regime.ToString());
            Assert(namesRegime, $"{regime}: real-pipeline diagnostic must name the regime. Diagnostics=[{string.Join("; ", triggerCandidate.Diagnostics)}].");
        }
    }

    /// <summary>MeanReverting through the same real chain: 5 models genuinely execute, and Reason is
    /// never UNSUPPORTED_REGIME (it may be READY, or one of the Sprint 15.7.1 NO_ACTION reasons,
    /// depending on the exact DynamicZScore the real Kalman/DynamicZScore models converge to for the
    /// fixture history - what matters here is only that it is never UNSUPPORTED_REGIME).</summary>
    private static void AssertRealPipelineMeanRevertingReasonIsNeverUnsupportedRegime()
    {
        var decisionResult = new DecisionResult { Winner = MarketState.MeanReverting, Confidence = 0.9, AmbiguityScore = 0.1 };
        MethodologySelection methodologySelection = new MethodologyEngine().Evaluate(decisionResult);

        var marketContext = new ScientificMarketContext(DateTime.UtcNow, 100m, BuildHistory(), CurrentPrice: 100m);
        var signalEngine = new SignalEngine();
        signalEngine.Process(marketContext, methodologySelection);

        ScientificAssessment? assessment = signalEngine.LastScientificAssessment;
        if (assessment is null)
            throw new InvalidOperationException("LastScientificAssessment must be populated after Process.");
        Assert(assessment.ExecutedModels.Count == 5,
            $"MeanReverting must still genuinely execute all 5 models through the real registry. Actual={assessment.ExecutedModels.Count}.");
        Assert(assessment.CoverageStatus == ScientificCoverageStatus.ModelsExecuted,
            $"MeanReverting must report ModelsExecuted. Actual={assessment.CoverageStatus}.");

        EntryTriggerCandidate? triggerCandidate = signalEngine.LastEntryTriggerCandidate;
        if (triggerCandidate is null)
            throw new InvalidOperationException("LastEntryTriggerCandidate must be populated after Process.");
        Assert(triggerCandidate.Assessment.Reason != EntryTriggerReason.UNSUPPORTED_REGIME,
            $"MeanReverting must never report UNSUPPORTED_REGIME through the real pipeline. Actual={triggerCandidate.Assessment.Reason}.");
    }

    // ── 3. Determinism (brief §26) / run isolation (brief §28) ──────────────────────────────────────

    /// <summary>Two independently-built EntryTriggerContext object graphs (not the same reference),
    /// same inputs, each run through its own new EntryTriggerBuilder - must produce a bit-identical
    /// Direction/Reason/diagnostic sequence. Proves the new UNSUPPORTED_REGIME path has no hidden
    /// nondeterminism (e.g. no reliance on DateTime.UtcNow, ordering, or mutable shared state for the
    /// VALUE of Reason/Direction/diagnostics - Timestamp fields are intentionally excluded from the
    /// comparison since EntryTriggerAssessment.Timestamp is documented to be DateTime.UtcNow at build
    /// time, not part of the deterministic decision surface).</summary>
    private static void AssertDeterministicAcrossTwoIndependentBuilds()
    {
        EntryTriggerCandidate run1 = Trigger(MarketState.StructuralBreak, ambiguityScore: 0.2, dynamicZScore: 1.5);
        EntryTriggerCandidate run2 = Trigger(MarketState.StructuralBreak, ambiguityScore: 0.2, dynamicZScore: 1.5);

        Assert(run1.Assessment.Direction == run2.Assessment.Direction,
            $"Direction must be bit-identical across two independent runs of the same scenario. run1={run1.Assessment.Direction}, run2={run2.Assessment.Direction}.");
        Assert(run1.Assessment.Reason == run2.Assessment.Reason,
            $"Reason must be bit-identical across two independent runs of the same scenario. run1={run1.Assessment.Reason}, run2={run2.Assessment.Reason}.");
        Assert(run1.Diagnostics.SequenceEqual(run2.Diagnostics),
            $"Diagnostics must be bit-identical (same content, same order) across two independent runs. run1=[{string.Join("; ", run1.Diagnostics)}], run2=[{string.Join("; ", run2.Diagnostics)}].");
    }

    /// <summary>A single, reused SignalEngine (and, more importantly, the EntryTriggerEngine/Builder it
    /// constructs a fresh instance of on every Process call - see EntryTriggerEngine.Process) must not
    /// leak the UNSUPPORTED_REGIME diagnostic/reason from one run into the next unrelated run, nor
    /// leak a MeanReverting BUY/SELL result into a following unsupported-regime run. Runs an unsupported
    /// regime then MeanReverting, then the reverse order, on the SAME SignalEngine instance.</summary>
    private static void AssertNoStateLeaksBetweenConsecutiveSignalEngineRuns()
    {
        var signalEngine = new SignalEngine();

        // Order 1: unsupported regime first, then MeanReverting.
        RunOnce(signalEngine, MarketState.Trending);
        EntryTriggerCandidate? second = signalEngine.LastEntryTriggerCandidate;
        RunOnce(signalEngine, MarketState.MeanReverting);
        EntryTriggerCandidate? afterMeanReverting = signalEngine.LastEntryTriggerCandidate;

        if (second is null || afterMeanReverting is null)
            throw new InvalidOperationException("LastEntryTriggerCandidate must be populated after every Process call.");

        Assert(afterMeanReverting.Assessment.Reason != EntryTriggerReason.UNSUPPORTED_REGIME,
            $"A MeanReverting run immediately following an unsupported-regime run on the SAME SignalEngine must not inherit UNSUPPORTED_REGIME. Actual={afterMeanReverting.Assessment.Reason}.");
        bool leakedTrendingDiagnostic = ContainsSubstring(afterMeanReverting.Diagnostics, "Trending");
        Assert(!leakedTrendingDiagnostic,
            $"MeanReverting run's diagnostics must not contain a stale reference to the prior Trending run. Diagnostics=[{string.Join("; ", afterMeanReverting.Diagnostics)}].");

        // Order 2 (reversed): MeanReverting first, then an unsupported regime.
        RunOnce(signalEngine, MarketState.MeanReverting);
        RunOnce(signalEngine, MarketState.RandomWalk);
        EntryTriggerCandidate? afterRandomWalk = signalEngine.LastEntryTriggerCandidate;
        if (afterRandomWalk is null)
            throw new InvalidOperationException("LastEntryTriggerCandidate must be populated after every Process call.");

        Assert(afterRandomWalk.Assessment.Reason == EntryTriggerReason.UNSUPPORTED_REGIME,
            $"A RandomWalk run immediately following a MeanReverting run on the SAME SignalEngine must still correctly report UNSUPPORTED_REGIME - the prior run's coverage/models must not leak forward. Actual={afterRandomWalk.Assessment.Reason}.");
        Assert(afterRandomWalk.Assessment.Direction == DirectionCandidate.NO_ACTION,
            $"A RandomWalk run must never inherit a directional (BUY/SELL) outcome from a prior MeanReverting run on the same engine instance. Actual={afterRandomWalk.Assessment.Direction}.");
    }

    private static void RunOnce(SignalEngine signalEngine, MarketState regime)
    {
        var decisionResult = new DecisionResult { Winner = regime, Confidence = 0.9, AmbiguityScore = 0.1 };
        MethodologySelection methodologySelection = new MethodologyEngine().Evaluate(decisionResult);
        var marketContext = new ScientificMarketContext(DateTime.UtcNow, 100m, BuildHistory(), CurrentPrice: 100m);
        signalEngine.Process(marketContext, methodologySelection);
    }

    // ── Helpers (same pattern as DecisionDirectionCoherenceTests.Trigger/BuildContext) ────────────────

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
