using System;
using System.Linq;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Entry;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.Presentation;
using IQIAIndicator.Engine.ScientificFusion;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Engine.Visualization;

namespace IQIAIndicator.Tests.Signal;

/// <summary>
/// Sprint 15.6, section 9: end-to-end coverage of the full chain this sprint closes -
/// Decision -&gt; Methodology -&gt; Entry -&gt; EntryTrigger -&gt; Direction -&gt; Visualization -&gt; ChartAnnotation -
/// using the real DecisionResult / MethodologyEngine / EntryTriggerEngine / VisualizationEngine /
/// ChartAnnotationEngine classes chained together, not hand-built substitutes for any of them. Only the
/// scientific-model layer (DynamicZScoreModel's own Kalman-filter convergence) is bypassed, by setting
/// EntryBusinessContext.DynamicZScore directly - the same deterministic technique Sprint 15.5's
/// DecisionDirectionCoherenceTests already established, needed because forcing a specific convergence
/// sign through a hand-crafted price history is not something this sprint should have to
/// reverse-engineer to prove propagation integrity.
/// </summary>
public static class DirectionEndToEndTests
{
    public static void RunAll()
    {
        AssertBuyScenarioReachesArrowUp();
        AssertSellScenarioReachesArrowDown();
        AssertNoActionScenarioReachesNoArrow();
    }

    /// <summary>Decision cohérente (MeanReverting, faible ambiguïté) -&gt; DynamicZScore négatif -&gt; BUY -&gt; Visualization BUY -&gt; Arrow Up.</summary>
    private static void AssertBuyScenarioReachesArrowUp()
    {
        ChartAnnotationCandidate result = RunChain(MarketState.MeanReverting, ambiguityScore: 0.1, dynamicZScore: -2.0);

        ChartAnnotation? arrow = FindArrow(result);
        Assert(arrow is not null, "BUY scenario: an Arrow annotation must reach ChartAnnotation.");
        Assert(arrow!.Payload.Metrics.TryGetValue("Direction", out var direction) && (string)direction == "BUY", "BUY scenario: Arrow must carry Direction=BUY.");
        Assert(arrow.Payload.Metrics.TryGetValue("ArrowDirection", out var arrowDirection) && (string)arrowDirection == "Arrow Up", "BUY scenario: Arrow must map to Arrow Up.");
    }

    /// <summary>Decision cohérente (MeanReverting, faible ambiguïté) -&gt; DynamicZScore positif -&gt; SELL -&gt; Visualization SELL -&gt; Arrow Down.</summary>
    private static void AssertSellScenarioReachesArrowDown()
    {
        ChartAnnotationCandidate result = RunChain(MarketState.MeanReverting, ambiguityScore: 0.1, dynamicZScore: 2.0);

        ChartAnnotation? arrow = FindArrow(result);
        Assert(arrow is not null, "SELL scenario: an Arrow annotation must reach ChartAnnotation.");
        Assert(arrow!.Payload.Metrics.TryGetValue("Direction", out var direction) && (string)direction == "SELL", "SELL scenario: Arrow must carry Direction=SELL.");
        Assert(arrow.Payload.Metrics.TryGetValue("ArrowDirection", out var arrowDirection) && (string)arrowDirection == "Arrow Down", "SELL scenario: Arrow must map to Arrow Down.");
    }

    /// <summary>Decision ambiguë (AmbiguityScore élevé) sur un régime par ailleurs supporté -&gt; NO_ACTION -&gt; aucune Arrow, même si DynamicZScore seul suggérerait BUY.</summary>
    private static void AssertNoActionScenarioReachesNoArrow()
    {
        ChartAnnotationCandidate result = RunChain(MarketState.MeanReverting, ambiguityScore: 0.9, dynamicZScore: -2.0);

        ChartAnnotation? arrow = FindArrow(result);
        Assert(arrow is null, "Ambiguous-decision scenario: no Arrow annotation may reach ChartAnnotation, even though DynamicZScore alone would suggest BUY.");
    }

    private static ChartAnnotationCandidate RunChain(MarketState winner, double ambiguityScore, double? dynamicZScore)
    {
        var decisionResult = new DecisionResult { Winner = winner, Confidence = 0.9, AmbiguityScore = ambiguityScore };
        MethodologySelection methodologySelection = new MethodologyEngine().Evaluate(decisionResult);

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

        var businessContext = new EntryBusinessContext(
            CurrentPrice: 100m,
            EstimatedEquilibrium: 100.0,
            DistanceToEquilibrium: 0.0,
            DynamicZScore: dynamicZScore,
            ScientificConfidence: 0.8,
            OpportunityPriority: 0.8,
            Methodology: methodologySelection.SelectedMethodology.Name,
            Timestamp: DateTime.UtcNow,
            SupportingEvidence: Array.Empty<string>(),
            BlockingIssues: Array.Empty<string>(),
            Diagnostics: Array.Empty<string>(),
            Warnings: Array.Empty<string>(),
            OpportunityReasons: Array.Empty<string>(),
            OpportunityStatus: OpportunityStatus.QUALIFIED);

        var entryTriggerContext = new EntryTriggerContext(businessContext, scientificAssessment, entryAssessment, entryCandidate, methodologySelection);

        // Real engines, chained: EntryTrigger -> Visualization -> ChartAnnotation. No engine here is a
        // hand-built substitute.
        EntryTriggerResult entryTriggerResult = new EntryTriggerEngine().Process(entryTriggerContext);
        VisualizationCandidate visualizationCandidate = new VisualizationEngine().Process(new VisualizationContext(entryTriggerResult.Candidate));
        return new ChartAnnotationEngine().Process(new ChartAnnotationContext(visualizationCandidate));
    }

    private static ChartAnnotation? FindArrow(ChartAnnotationCandidate candidate)
        => candidate.Annotations.FirstOrDefault(annotation => annotation.AnnotationType == AnnotationType.Arrow);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
