using System;
using IQIAIndicator.Core;
using IQIAIndicator.Core.Calibration;
using IQIAIndicator.Core.Observability;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Entry;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.State;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.Presentation;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.ScientificFusion;
using IQIAIndicator.Engine.TradePlan;
using IQIAIndicator.Engine.Visualization;
using ScientificMarketContext = IQIAIndicator.Engine.ScientificModels.Abstractions.MarketContext;

namespace IQIAIndicator.Visualization.State;

/// <summary>
/// Façade en lecture seule sur tout ce que le pipeline a déjà produit pour le bar courant.
/// Ce n'est pas un nouveau DTO métier : uniquement un regroupement de références vers des
/// objets déjà construits par IQIAIndicator.OnCalculate, passé une seule fois à
/// DashboardManager.Draw au lieu des listes de paramètres qu'IQIAFusionDashboard/
/// IQIAPipelineDashboard recevaient séparément.
/// Aucun dashboard n'écrit dans cet objet : consommation seule.
/// </summary>
internal sealed record DashboardContext
{
    public required int BarIndex { get; init; }
    public required DateTime Timestamp { get; init; }
    public required int AvailableEvidenceCount { get; init; }

    public ScientificMarketContext? ScientificMarketContext { get; init; }
    public ExecutionContext? Execution { get; init; }

    public EvidenceSet? Evidence { get; init; }
    public FusionResult? FusionResult { get; init; }
    public FusionSnapshot? FusionSnapshot { get; init; }
    public DecisionResult? DecisionResult { get; init; }
    public MethodologySelection? MethodologySelection { get; init; }
    public ScientificAssessment? ScientificAssessment { get; init; }
    public EntryCandidate? EntryCandidate { get; init; }
    public EntryTriggerCandidate? EntryTriggerCandidate { get; init; }
    public EntryTiming? EntryTiming { get; init; }
    public VisualizationCandidate? VisualizationCandidate { get; init; }
    public ChartAnnotationCandidate? ChartAnnotationCandidate { get; init; }
    public OpportunityPresentation? OpportunityPresentation { get; init; }
    public TradePlan? TradePlan { get; init; }

    public bool RendererCalled { get; init; }
    public int AnnotationsRendered { get; init; }
    public DateTime? LastRenderTime { get; init; }

    public PipelineTraceRun? PipelineTrace { get; init; }
    public string PipelineTraceReport { get; init; } = string.Empty;

    public bool EnablePipelineTracing { get; init; }

    public bool EnableScientificDataset { get; init; }
    public ScientificDatasetCollector? DatasetCollector { get; init; }
    public ScientificDatasetSession? DatasetSession { get; init; }
    public string DatasetOutputDirectory { get; init; } = string.Empty;
    public DateTime? DatasetStartTime { get; init; }

    /// <summary>Vrai une fois que les artefacts principaux du pipeline sont disponibles pour ce bar.</summary>
    public bool HasPipelineOutput =>
        Evidence is not null && FusionResult is not null && FusionSnapshot is not null && DecisionResult is not null;
}
