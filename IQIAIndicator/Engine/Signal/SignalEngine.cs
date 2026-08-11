using System.Linq;
using IQIAIndicator.Core;
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
    private readonly EntryTriggerEngine _entryTriggerEngine = new();
    private readonly VisualizationEngine _visualizationEngine = new();
    private readonly ChartAnnotationEngine _chartAnnotationEngine = new();
    private readonly OpportunityPresentationEngine _opportunityPresentationEngine = new();

    public SignalEngine()
    {
        _registry = new ScientificModelRegistry();
    }

    public ChartAnnotationCandidate? LastChartAnnotationCandidate { get; private set; }
    public ScientificAssessment? LastScientificAssessment { get; private set; }
    public EntryCandidate? LastEntryCandidate { get; private set; }
    public EntryTriggerCandidate? LastEntryTriggerCandidate { get; private set; }
    public EntryTiming? LastEntryTiming { get; private set; }
    public VisualizationCandidate? LastVisualizationCandidate { get; private set; }
    public OpportunityPresentation? LastOpportunityPresentation { get; private set; }

    public OpportunityPresentation Process(ScientificMarketContext marketContext, MethodologySelection methodologySelection)
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
            ScientificModelResult result = model.Evaluate(currentContext);
            scientificResults.Add(result);
        }

        ScientificAssessment scientificAssessment = _scientificFusionEngine.Assess(scientificResults);
        EntryCandidate entryCandidate = _entryEngine.Process(new EntryContext(scientificAssessment));

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

        EntryTriggerResult entryTriggerResult = _entryTriggerEngine.Process(new EntryTriggerContext(businessContext, scientificAssessment, entryCandidate.Assessment, entryCandidate, methodologySelection));
        VisualizationCandidate visualizationCandidate = _visualizationEngine.Process(new VisualizationContext(entryTriggerResult.Candidate));
        ChartAnnotationCandidate chartAnnotationCandidate = _chartAnnotationEngine.Process(new ChartAnnotationContext(visualizationCandidate));
        OpportunityPresentation opportunityPresentation = _opportunityPresentationEngine.Process(new OpportunityPresentationContext(chartAnnotationCandidate));

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
