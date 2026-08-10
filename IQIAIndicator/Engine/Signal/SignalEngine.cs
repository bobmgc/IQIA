using System.Linq;
using IQIAIndicator.Core;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Entry;
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
        VisualizationCandidate visualizationCandidate = _visualizationEngine.Process(new VisualizationContext(entryCandidate));
        ChartAnnotationCandidate chartAnnotationCandidate = _chartAnnotationEngine.Process(new ChartAnnotationContext(visualizationCandidate));
        OpportunityPresentation opportunityPresentation = _opportunityPresentationEngine.Process(new OpportunityPresentationContext(chartAnnotationCandidate));

        LastScientificAssessment = scientificAssessment;
        LastEntryCandidate = entryCandidate;
        LastVisualizationCandidate = visualizationCandidate;
        LastChartAnnotationCandidate = chartAnnotationCandidate;
        LastOpportunityPresentation = opportunityPresentation;

        LogDebugDetails(marketContext, methodologySelection, scientificResults, scientificAssessment, entryCandidate, visualizationCandidate, chartAnnotationCandidate, opportunityPresentation);

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
