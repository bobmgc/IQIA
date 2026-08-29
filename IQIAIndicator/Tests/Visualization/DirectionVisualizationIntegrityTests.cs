using System;
using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Engine.Entry;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Presentation;
using IQIAIndicator.Engine.ScientificFusion;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Engine.Visualization;

namespace IQIAIndicator.Tests.Visualization;

/// <summary>
/// Sprint 15.6 (BC-01 / BC-07). Direct, unit-level coverage of Direction propagation from the single
/// source of truth (EntryTriggerAssessment.Direction, established by Sprint 15.5's decision-coherence
/// gate) through VisualizationAssessment into ChartAnnotation - and proof that no second, competing
/// direction computation exists anywhere in that chain.
///
/// Note on the "don't reread DynamicZScore" requirement: EntryTriggerCandidate (the only input
/// VisualizationAssessmentBuilder.Populate receives) structurally does not carry DynamicZScore or
/// EntryBusinessContext at all - Assessment.Direction is genuinely the only directional information
/// reachable from this point on. AssertVisualizationNeverRecomputesDirectionFromConflictingMetrics
/// below proves the same property using the metrics EntryTriggerAssessment does carry
/// (EstimatedEquilibrium/DistanceToEquilibrium), by making them suggest the opposite of the explicit
/// Direction and confirming Direction still wins verbatim.
/// </summary>
public static class DirectionVisualizationIntegrityTests
{
    public static void RunAll()
    {
        AssertBuyDirectionProducesArrowUp();
        AssertSellDirectionProducesArrowDown();
        AssertNoActionProducesNoArrow();
        AssertMissingEntryTriggerCandidateProducesNoDirectionAndNoArrow();
        AssertVisualizationNeverRecomputesDirectionFromConflictingMetrics();
        AssertArrowAnnotationUsesTheExistingCurrentBarAnchor();
    }

    private static void AssertBuyDirectionProducesArrowUp()
    {
        VisualizationAssessment visualization = Visualize(DirectionCandidate.BUY_CANDIDATE);
        Assert(visualization.Direction == DirectionCandidate.BUY_CANDIDATE, $"Visualization must receive BUY_CANDIDATE unchanged. Actual={visualization.Direction}.");

        ChartAnnotation? arrow = FindArrow(Annotate(visualization));
        Assert(arrow is not null, "A BUY direction must produce an Arrow annotation.");
        Assert(arrow!.Payload.Metrics.TryGetValue("Direction", out var direction) && (string)direction == "BUY", "The Arrow's Direction metric must read BUY.");
        Assert(arrow.Payload.Metrics.TryGetValue("ArrowDirection", out var arrowDirection) && (string)arrowDirection == "Arrow Up", "BUY must map to Arrow Up.");
    }

    private static void AssertSellDirectionProducesArrowDown()
    {
        VisualizationAssessment visualization = Visualize(DirectionCandidate.SELL_CANDIDATE);
        Assert(visualization.Direction == DirectionCandidate.SELL_CANDIDATE, $"Visualization must receive SELL_CANDIDATE unchanged. Actual={visualization.Direction}.");

        ChartAnnotation? arrow = FindArrow(Annotate(visualization));
        Assert(arrow is not null, "A SELL direction must produce an Arrow annotation.");
        Assert(arrow!.Payload.Metrics.TryGetValue("Direction", out var direction) && (string)direction == "SELL", "The Arrow's Direction metric must read SELL.");
        Assert(arrow.Payload.Metrics.TryGetValue("ArrowDirection", out var arrowDirection) && (string)arrowDirection == "Arrow Down", "SELL must map to Arrow Down.");
    }

    private static void AssertNoActionProducesNoArrow()
    {
        VisualizationAssessment visualization = Visualize(DirectionCandidate.NO_ACTION);
        Assert(visualization.Direction == DirectionCandidate.NO_ACTION, "Visualization must receive NO_ACTION unchanged.");

        ChartAnnotation? arrow = FindArrow(Annotate(visualization));
        Assert(arrow is null, "NO_ACTION must never produce an Arrow annotation.");
    }

    private static void AssertMissingEntryTriggerCandidateProducesNoDirectionAndNoArrow()
    {
        var builder = new VisualizationAssessmentBuilder();
        builder.Populate(null!);
        VisualizationAssessment visualization = builder.Build();

        Assert(visualization.Direction is null, $"With no EntryTriggerCandidate at all, Direction must be null, not a fabricated default. Actual={visualization.Direction}.");

        ChartAnnotation? arrow = FindArrow(Annotate(visualization));
        Assert(arrow is null, "A missing Direction must never produce an Arrow annotation.");
    }

    /// <summary>
    /// The specific regression this sprint must prevent: even when other numeric fields on the
    /// assessment would suggest the OPPOSITE direction under a naive heuristic (price far below
    /// estimated equilibrium "should" suggest BUY), Visualization must still report exactly
    /// Assessment.Direction (forced here to SELL) - proof there is no second direction computation
    /// anywhere in Visualization/ChartAnnotationBuilder.
    /// </summary>
    private static void AssertVisualizationNeverRecomputesDirectionFromConflictingMetrics()
    {
        EntryTriggerCandidate candidate = TriggerWithExplicitDirection(
            DirectionCandidate.SELL_CANDIDATE,
            estimatedEquilibrium: 100.0,
            distanceToEquilibrium: -5.0); // price 5 below "equilibrium" - a naive reader would guess BUY.

        var builder = new VisualizationAssessmentBuilder();
        builder.Populate(candidate);
        VisualizationAssessment visualization = builder.Build();

        Assert(visualization.Direction == DirectionCandidate.SELL_CANDIDATE,
            $"Visualization must reflect Assessment.Direction exactly (SELL) regardless of what EstimatedEquilibrium/DistanceToEquilibrium might otherwise suggest. Actual={visualization.Direction}.");
    }

    private static void AssertArrowAnnotationUsesTheExistingCurrentBarAnchor()
    {
        VisualizationAssessment visualization = Visualize(DirectionCandidate.BUY_CANDIDATE);
        ChartAnnotation? arrow = FindArrow(Annotate(visualization));

        Assert(arrow is not null, "A BUY direction must produce an Arrow annotation.");
        Assert(arrow!.Anchor == AnnotationAnchor.CurrentBar,
            $"The Arrow must use the same AnnotationAnchor.CurrentBar contract every other annotation already uses - no new, invented positioning mechanism. Actual={arrow.Anchor}.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static VisualizationAssessment Visualize(DirectionCandidate direction)
    {
        EntryTriggerCandidate candidate = TriggerWithExplicitDirection(direction, estimatedEquilibrium: 100.0, distanceToEquilibrium: 0.0);
        var builder = new VisualizationAssessmentBuilder();
        builder.Populate(candidate);
        return builder.Build();
    }

    private static IReadOnlyList<ChartAnnotation> Annotate(VisualizationAssessment visualization)
    {
        var visualizationCandidate = new VisualizationCandidate(visualization, DateTime.UtcNow, Array.Empty<string>(), Array.Empty<string>());
        var builder = new ChartAnnotationBuilder();
        builder.Populate(visualizationCandidate);
        return builder.Build().Annotations;
    }

    private static ChartAnnotation? FindArrow(IReadOnlyList<ChartAnnotation> annotations)
        => annotations.FirstOrDefault(annotation => annotation.AnnotationType == AnnotationType.Arrow);

    /// <summary>
    /// Builds a real EntryTriggerCandidate whose Assessment.Direction is set directly to the requested
    /// value (EntryTriggerAssessment is plain data, so this needs no engine call), keeping
    /// OpportunityStatus=QUALIFIED throughout so it is never mistaken for the WATCHLIST short-circuit
    /// already covered by Sprint 15.5's DecisionDirectionCoherenceTests.
    /// </summary>
    private static EntryTriggerCandidate TriggerWithExplicitDirection(
        DirectionCandidate direction,
        double estimatedEquilibrium,
        double distanceToEquilibrium)
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
            OpportunityStatus: OpportunityStatus.QUALIFIED,
            OpportunityPriority: 0.8,
            OpportunityReasons: Array.Empty<string>(),
            BlockingIssues: Array.Empty<string>(),
            SupportingEvidence: Array.Empty<string>(),
            Warnings: Array.Empty<string>(),
            Diagnostics: Array.Empty<string>());

        var entryCandidate = new EntryCandidate(
            entryAssessment,
            DateTime.UtcNow,
            OpportunityStatus.QUALIFIED,
            OpportunityPriority: 0.8,
            OpportunityReasons: Array.Empty<string>(),
            Warnings: Array.Empty<string>(),
            Diagnostics: Array.Empty<string>());

        var assessment = new EntryTriggerAssessment(
            EntryTriggerStatus.READY,
            direction,
            ScientificConfidence: 0.8,
            OpportunityPriority: 0.8,
            Reason: EntryTriggerReason.READY,
            EstimatedEquilibrium: estimatedEquilibrium,
            DistanceToEquilibrium: distanceToEquilibrium,
            Timestamp: DateTime.UtcNow);

        return new EntryTriggerCandidate(assessment, entryCandidate, CurrentPrice: 100m, Warnings: Array.Empty<string>(), Diagnostics: Array.Empty<string>(), CreatedAt: DateTime.UtcNow);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
