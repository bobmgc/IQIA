using System.Globalization;
using System.Linq;
using IQIAIndicator.Core;
using IQIAIndicator.Core.Observability;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Entry;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.Presentation;
using IQIAIndicator.Engine.ScientificFusion;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Engine.ScientificModels.Registry;
using IQIAIndicator.Engine.Visualization;

namespace IQIAIndicator.Engine.Signal;

using ScientificMarketContext = global::IQIAIndicator.Engine.ScientificModels.Abstractions.MarketContext;

public sealed class SignalEngine
{
    private readonly ILogger _logger = NullLogger.Instance;
    private readonly ScientificModelRegistry _registry;
    private readonly ScientificFusionEngine _scientificFusionEngine = new();
    private readonly EntryEngine _entryEngine = new();
    private readonly EntryTriggerEngine _entryTriggerEngine;
    private readonly VisualizationEngine _visualizationEngine = new();
    private readonly ChartAnnotationEngine _chartAnnotationEngine = new();
    private readonly OpportunityPresentationEngine _opportunityPresentationEngine = new();
    private readonly IPipelineTraceCollector _traceCollector;

    /// <summary><paramref name="ambiguityGateThreshold"/>: Sprint 15.25 (Lot 14.10, P0-3), an explicit,
    /// typed, optional override of EntryTriggerBuilder's production ambiguity gate (default:
    /// EntryTriggerBuilder.AmbiguityGateThreshold, i.e. identical behaviour to every SignalEngine that
    /// existed before this lot when this parameter is omitted - see
    /// Backtest.BacktestEngine.RunSignalPipeline's PipelineParameterOverrides overload for the one place a
    /// calibration experiment ever supplies a different value).</summary>
    public SignalEngine(IPipelineTraceCollector? traceCollector = null, double ambiguityGateThreshold = EntryTriggerBuilder.AmbiguityGateThreshold)
    {
        _registry = new ScientificModelRegistry();
        _traceCollector = traceCollector ?? NullPipelineTraceCollector.Instance;
        _entryTriggerEngine = new EntryTriggerEngine(ambiguityGateThreshold);
    }

    public ChartAnnotationCandidate? LastChartAnnotationCandidate { get; private set; }
    public ScientificAssessment? LastScientificAssessment { get; private set; }
    public EntryCandidate? LastEntryCandidate { get; private set; }
    public EntryTriggerCandidate? LastEntryTriggerCandidate { get; private set; }
    public EntryTiming? LastEntryTiming { get; private set; }
    public VisualizationCandidate? LastVisualizationCandidate { get; private set; }
    public OpportunityPresentation? LastOpportunityPresentation { get; private set; }

    public OpportunityPresentation Process(ScientificMarketContext marketContext, MethodologySelection methodologySelection)
        => Process(marketContext, methodologySelection, null);

    public OpportunityPresentation Process(
        ScientificMarketContext marketContext,
        MethodologySelection methodologySelection,
        PipelineTraceRun? trace)
    {
        ArgumentNullException.ThrowIfNull(marketContext);
        ArgumentNullException.ThrowIfNull(methodologySelection);

        IReadOnlyList<IScientificModel> activeModels = _registry.Resolve(methodologySelection);
        List<ScientificModelResult> scientificResults = [];
        var currentContext = new ScientificModelContext(
            marketContext,
            methodologySelection.DecisionResult,
            methodologySelection,
            Array.Empty<ScientificModelResult>());

        foreach (IScientificModel model in activeModels)
        {
            currentContext = currentContext with { ScientificResults = scientificResults.AsReadOnly() };
            PipelineTraceScope modelTrace = trace is null
                ? default
                : _traceCollector.BeginStage(trace, PipelineTraceStage.ScientificModels);
            DateTime modelStartedAt = DateTime.UtcNow;
            ScientificModelResult result;
            try
            {
                result = model.Evaluate(currentContext);
                if (trace is not null)
                {
                    modelTrace.Complete(
                        PipelineTraceDetails.Create(
                            ("Model", result.ModelName),
                            ("Success", result.Success),
                            ("Score", result.Score),
                            ("Confidence", "N/A: ScientificModelResult has no Confidence property"),
                            ("ElapsedMs", (DateTime.UtcNow - modelStartedAt).TotalMilliseconds),
                            ("MetricsCount", result.Metrics?.Count ?? 0),
                            ("Explanation", result.Explanation)),
                        result.Metrics?.Count ?? 0);
                }
            }
            catch (Exception exception)
            {
                modelTrace.Fail(exception);
                throw;
            }
            scientificResults.Add(result);
        }

        ScientificAssessment scientificAssessment;
        PipelineTraceScope fusionTrace = trace is null ? default : _traceCollector.BeginStage(trace, PipelineTraceStage.ScientificFusion);
        try
        {
            scientificAssessment = _scientificFusionEngine.Assess(scientificResults);
            if (trace is not null)
            {
                fusionTrace.Complete(
                    PipelineTraceDetails.Create(
                        ("OverallConfidence", scientificAssessment.OverallConfidence),
                        ("SuccessfulModels", string.Join(", ", scientificAssessment.SuccessfulModels)),
                        ("FailedModels", string.Join(", ", scientificAssessment.FailedModels)),
                        ("EvidenceAgreement", string.Join(", ", scientificAssessment.EvidenceAgreement)),
                        ("EvidenceConflict", string.Join(", ", scientificAssessment.EvidenceConflict)),
                        ("MissingEvidence", string.Join(", ", scientificAssessment.MissingEvidence)),
                        ("Diagnostics", scientificAssessment.Diagnostics)),
                    scientificAssessment.MissingEvidence.Count + scientificAssessment.EvidenceConflict.Count);
            }
        }
        catch (Exception exception)
        {
            fusionTrace.Fail(exception);
            throw;
        }

        EntryCandidate entryCandidate;
        PipelineTraceScope entryTrace = trace is null ? default : _traceCollector.BeginStage(trace, PipelineTraceStage.Entry);
        try
        {
            entryCandidate = _entryEngine.Process(new EntryContext(scientificAssessment));
            if (trace is not null)
            {
                entryTrace.Complete(
                    PipelineTraceDetails.Create(
                        ("OpportunityStatus", entryCandidate.OpportunityStatus),
                        ("Priority", entryCandidate.OpportunityPriority),
                        ("EntryReadiness", entryCandidate.Assessment.EntryReadiness),
                        ("BlockingIssues", string.Join(", ", entryCandidate.Assessment.BlockingIssues)),
                        ("SupportingEvidence", string.Join(", ", entryCandidate.Assessment.SupportingEvidence))),
                    entryCandidate.Assessment.Diagnostics.Count);
            }
        }
        catch (Exception exception)
        {
            entryTrace.Fail(exception);
            throw;
        }

        // Build EntryBusinessContext from available upstream data (no recalculation)
        decimal currentPrice = marketContext.CurrentPrice;
        double? estimatedEquilibrium = null;
        double? distanceToEquilibrium = null;
        double? dynamicZScore = null;

        foreach (var result in scientificResults)
        {
            if (result.Metrics is null) continue;

            if (string.Equals(result.ModelName, "KalmanFilterModel", StringComparison.OrdinalIgnoreCase) &&
                result.Metrics.TryGetValue("EstimatedMean", out var em) && em is double emd && double.IsFinite(emd))
            {
                estimatedEquilibrium = emd;
            }

            if (result.Metrics.TryGetValue("DistanceToEquilibrium", out var dte) && dte is double dted && double.IsFinite(dted))
            {
                distanceToEquilibrium = dted;
            }

            if (string.Equals(result.ModelName, "DynamicZScoreModel", StringComparison.OrdinalIgnoreCase) &&
                result.Metrics.TryGetValue("DynamicZScore", out var dz) && dz is double dzv && double.IsFinite(dzv))
            {
                dynamicZScore = dzv;
            }
        }

        string? methodology = methodologySelection?.SelectedMethodology?.Name;

        // Collect supporting evidence and diagnostics from upstream
        var supporting = new List<string>();
        if (scientificAssessment.SuccessfulModels is not null) supporting.AddRange(scientificAssessment.SuccessfulModels);
        if (entryCandidate.Assessment.SupportingEvidence is not null) supporting.AddRange(entryCandidate.Assessment.SupportingEvidence);

        var diagnostics = new List<string>();
        if (!string.IsNullOrWhiteSpace(scientificAssessment.Diagnostics)) diagnostics.Add(scientificAssessment.Diagnostics);
        if (entryCandidate.Assessment.Diagnostics is not null) diagnostics.AddRange(entryCandidate.Assessment.Diagnostics);

        var warnings = new List<string>();
        if (entryCandidate.Warnings is not null) warnings.AddRange(entryCandidate.Warnings);
        if (entryCandidate.Assessment.Warnings is not null) warnings.AddRange(entryCandidate.Assessment.Warnings);

        var businessContext = new EntryBusinessContext(
            currentPrice,
            estimatedEquilibrium,
            distanceToEquilibrium,
            dynamicZScore,
            scientificAssessment.OverallConfidence,
            entryCandidate.OpportunityPriority,
            methodology,
            marketContext.Timestamp,
            supporting.AsReadOnly(),
            entryCandidate.Assessment.BlockingIssues ?? Array.Empty<string>(),
            diagnostics.AsReadOnly(),
            warnings.AsReadOnly(),
            entryCandidate.OpportunityReasons ?? Array.Empty<string>(),
            entryCandidate.OpportunityStatus);

        EntryTriggerResult entryTriggerResult;
        PipelineTraceScope triggerTrace = trace is null ? default : _traceCollector.BeginStage(trace, PipelineTraceStage.EntryTrigger);
        try
        {
            entryTriggerResult = _entryTriggerEngine.Process(new EntryTriggerContext(businessContext, scientificAssessment, entryCandidate.Assessment, entryCandidate, methodologySelection));
            if (trace is not null)
            {
                // Sprint 15.7.1: DynamicZScore and Ambiguity are read straight from the pipeline (never
                // recomputed) so a NO_ACTION bar can be diagnosed from the trace alone - e.g. Reason=
                // PRICE_AT_EQUILIBRIUM with DynamicZScore=0.000000, or Reason=DECISION_AMBIGUOUS with
                // the Ambiguity value that triggered it.
                string dynamicZScoreText = businessContext.DynamicZScore is double dz && double.IsFinite(dz)
                    ? dz.ToString("F6", CultureInfo.InvariantCulture)
                    : "N/A";
                string ambiguityText = methodologySelection?.DecisionResult is { } decisionResult
                    ? decisionResult.AmbiguityScore.ToString("F3", CultureInfo.InvariantCulture)
                    : "N/A";

                triggerTrace.Complete(
                    PipelineTraceDetails.Create(
                        ("TriggerStatus", entryTriggerResult.Candidate.Assessment.TriggerStatus),
                        ("DynamicZScore", dynamicZScoreText),
                        ("Direction", entryTriggerResult.Candidate.Assessment.Direction),
                        ("Reason", entryTriggerResult.Candidate.Assessment.Reason),
                        ("Ambiguity", ambiguityText),
                        ("EstimatedEquilibrium", entryTriggerResult.Candidate.Assessment.EstimatedEquilibrium),
                        ("DistanceToEquilibrium", entryTriggerResult.Candidate.Assessment.DistanceToEquilibrium),
                        ("Confidence", entryTriggerResult.Candidate.Assessment.ScientificConfidence)),
                    entryTriggerResult.Candidate.Diagnostics.Count + entryTriggerResult.Candidate.Warnings.Count);
            }
        }
        catch (Exception exception)
        {
            triggerTrace.Fail(exception);
            throw;
        }

        VisualizationCandidate visualizationCandidate;
        PipelineTraceScope visualizationTrace = trace is null ? default : _traceCollector.BeginStage(trace, PipelineTraceStage.Visualization);
        try
        {
            visualizationCandidate = _visualizationEngine.Process(new VisualizationContext(entryTriggerResult.Candidate));
            if (trace is not null)
            {
                visualizationTrace.Complete(
                    PipelineTraceDetails.Create(
                        ("DisplayStatus", visualizationCandidate.Assessment.DisplayStatus),
                        ("Visibility", visualizationCandidate.Assessment.DisplayStatus),
                        // Sprint 15.6 (section 10): Direction carried through unchanged from
                        // EntryTriggerAssessment.Direction - exposed here (the existing pipeline trace
                        // mechanism) so it can be cross-checked against ATAS's rendered output during
                        // manual runtime validation, without a separate debug surface.
                        ("Direction", visualizationCandidate.Assessment.Direction?.ToString() ?? "None"),
                        ("Diagnostics", string.Join(", ", visualizationCandidate.Diagnostics))),
                    visualizationCandidate.Diagnostics.Count + visualizationCandidate.Warnings.Count);
            }
        }
        catch (Exception exception)
        {
            visualizationTrace.Fail(exception);
            throw;
        }

        ChartAnnotationCandidate chartAnnotationCandidate;
        PipelineTraceScope presentationTrace = trace is null ? default : _traceCollector.BeginStage(trace, PipelineTraceStage.ChartAnnotation);
        try
        {
            chartAnnotationCandidate = _chartAnnotationEngine.Process(new ChartAnnotationContext(visualizationCandidate));
            if (trace is not null)
            {
                // Sprint 15.6 (section 10): the primary (status) annotation is always Annotations[0];
                // the directional Arrow, when present, is a second entry added by
                // ChartAnnotationBuilder.TryCreateDirectionAnnotation - surfaced explicitly here so
                // manual ATAS runtime validation can confirm Direction/BarIndex-anchor/ArrowDirection
                // without a separate debug surface (reusing this existing trace mechanism).
                ChartAnnotation? arrow = chartAnnotationCandidate.Annotations
                    .FirstOrDefault(annotation => annotation.AnnotationType == AnnotationType.Arrow);
                presentationTrace.Complete(
                    PipelineTraceDetails.Create(
                        ("AnnotationType", chartAnnotationCandidate.Annotations.Count == 0 ? "None" : chartAnnotationCandidate.Annotations[0].AnnotationType),
                        ("Visibility", chartAnnotationCandidate.Annotations.Count == 0 ? "None" : chartAnnotationCandidate.Annotations[0].Visibility),
                        ("Annotations", chartAnnotationCandidate.Annotations.Count),
                        ("HasDirectionArrow", arrow is not null),
                        ("ArrowAnchor", arrow?.Anchor.ToString() ?? "None"),
                        ("ArrowDirection", arrow is not null && arrow.Payload.Metrics.TryGetValue("ArrowDirection", out var arrowDirection) ? arrowDirection : "None")),
                    chartAnnotationCandidate.Diagnostics.Count + chartAnnotationCandidate.Warnings.Count);
            }
        }
        catch (Exception exception)
        {
            presentationTrace.Fail(exception);
            throw;
        }

        OpportunityPresentation opportunityPresentation;
        PipelineTraceScope opportunityTrace = trace is null ? default : _traceCollector.BeginStage(trace, PipelineTraceStage.Presentation);
        try
        {
            opportunityPresentation = _opportunityPresentationEngine.Process(new OpportunityPresentationContext(chartAnnotationCandidate));
            if (trace is not null)
            {
                opportunityTrace.Complete(
                    PipelineTraceDetails.Create(
                        ("OpportunityStatus", opportunityPresentation.OpportunityStatus),
                        ("Priority", opportunityPresentation.OpportunityPriority),
                        ("SignalLabel", opportunityPresentation.SignalLabel),
                        ("RiskLabel", opportunityPresentation.RiskLabel)),
                    opportunityPresentation.Diagnostics.Length + opportunityPresentation.Warnings.Length);
            }
        }
        catch (Exception exception)
        {
            opportunityTrace.Fail(exception);
            throw;
        }

        LastScientificAssessment = scientificAssessment;
        LastEntryCandidate = entryCandidate;
        LastEntryTriggerCandidate = entryTriggerResult.Candidate;
        LastEntryTiming = entryTriggerResult.Timing;
        LastVisualizationCandidate = visualizationCandidate;
        LastChartAnnotationCandidate = chartAnnotationCandidate;
        LastOpportunityPresentation = opportunityPresentation;

        LogDebugDetails(marketContext, methodologySelection!, scientificResults, scientificAssessment, entryCandidate, entryTriggerResult, visualizationCandidate, chartAnnotationCandidate, opportunityPresentation);

        return opportunityPresentation;
    }

    public SignalCandidate Evaluate(MethodologySelection methodologySelection)
    {
        ArgumentNullException.ThrowIfNull(methodologySelection);

        IReadOnlyList<IScientificModel> activeModels = _registry.Resolve(methodologySelection);
        return EvaluateInternal(methodologySelection, activeModels, new ScientificMarketContext(DateTime.UtcNow, 0m, Array.Empty<decimal>()));
    }

    public SignalCandidate Evaluate(MethodologySelection methodologySelection, IReadOnlyList<IScientificModel> models)
    {
        ArgumentNullException.ThrowIfNull(methodologySelection);
        ArgumentNullException.ThrowIfNull(models);

        return EvaluateInternal(methodologySelection, models, new ScientificMarketContext(DateTime.UtcNow, 0m, Array.Empty<decimal>()));
    }

    private void LogDebugDetails(
        ScientificMarketContext marketContext,
        MethodologySelection methodologySelection,
        IReadOnlyList<ScientificModelResult> scientificResults,
        ScientificAssessment scientificAssessment,
        EntryCandidate entryCandidate,
        EntryTriggerResult entryTriggerResult,
        VisualizationCandidate visualizationCandidate,
        ChartAnnotationCandidate chartAnnotationCandidate,
        OpportunityPresentation opportunityPresentation)
    {
        _logger.Info("========== SIGNAL ENGINE ==========");
        _logger.Info($"Methodology : {methodologySelection.SelectedMethodology.Name}");
        _logger.Info($"Scientific Results : {scientificResults.Count}");
        _logger.Info($"Scientific Success : {scientificResults.Count(result => result.Success)}");
        _logger.Info($"Scientific Failure : {scientificResults.Count(result => !result.Success)}");
        _logger.Info($"Entry Status : {entryCandidate.OpportunityStatus}");
        _logger.Info($"EntryTrigger Status : {entryTriggerResult.Candidate.Assessment.TriggerStatus}");
        _logger.Info($"Opportunity Status : {opportunityPresentation.OpportunityStatus}");
        _logger.Info($"Visualization : {visualizationCandidate.Assessment.DisplayStatus}");
        _logger.Info($"Presentation : {opportunityPresentation.Title}");
        _logger.Info($"ScientificAssessment : {scientificAssessment.Diagnostics}");
        _logger.Info($"EntryAssessment : {entryCandidate.Assessment.EntryReadiness}");
        _logger.Info($"ChartAnnotationCandidate Annotations : {chartAnnotationCandidate.Annotations?.Count ?? 0}");
        int annotationWarningCount = chartAnnotationCandidate.Warnings?.Count ?? 0;
        _logger.Info($"Annotations Warnings : {annotationWarningCount}");
        int presentationDiagnosticCount = opportunityPresentation.Diagnostics.Length;
        _logger.Info($"Presentation Diagnostics : {presentationDiagnosticCount}");

        foreach (ScientificModelResult result in scientificResults)
        {
            _logger.Info("-------------------------");
            _logger.Info($"Model : {result.ModelName}");
            _logger.Info($"Executed : true");
            _logger.Info($"Success : {result.Success}");
            _logger.Info($"Score : {result.Score}");
            _logger.Info($"Metrics Count : {result.Metrics?.Count ?? 0}");
            _logger.Info($"Diagnostics : {result.Explanation}");
        }

        _logger.Info("=================================");
    }

    private SignalCandidate EvaluateInternal(
        MethodologySelection methodologySelection,
        IReadOnlyList<IScientificModel> models,
        ScientificMarketContext marketContext)
    {
        ArgumentNullException.ThrowIfNull(methodologySelection);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(marketContext);

        var context = new ScientificModelContext(
            marketContext,
            methodologySelection.DecisionResult,
            methodologySelection,
            Array.Empty<ScientificModelResult>());

        var builder = new SignalResultBuilder();
        var scientificResults = new List<ScientificModelResult>();
        var currentContext = context;

        foreach (IScientificModel model in models)
        {
            currentContext = currentContext with { ScientificResults = scientificResults.AsReadOnly() };
            ScientificModelResult result = model.Evaluate(currentContext);
            scientificResults.Add(result);
            builder.AddScientificResult(result);
        }

        return builder.Build(
            DateTime.UtcNow,
            methodologySelection.SelectedMethodology,
            $"Signal pipeline executed for {methodologySelection.DecisionResult.Winner} using {models.Count} scientific model(s)");
    }
}
