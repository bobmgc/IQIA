using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using ATAS.Indicators;
using IQIAIndicator.Core;
using IQIAIndicator.Core.Observability;
using IQIAIndicator.Core.Calibration;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Entry;
using IQIAIndicator.Engine.Fusion;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Fusion.State;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.Presentation;
using IQIAIndicator.Engine.Regime;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Signal;
using IQIAIndicator.Engine.ScientificFusion;
using IQIAIndicator.Engine.Visualization;
using IQIAIndicator.Infrastructure.ATAS;
using IQIAIndicator.Visualization.Dashboards;
using IQIAIndicator.Visualization.State;
using OFT.Rendering.Context;
using OFT.Rendering.Control;
using DecisionRules = IQIAIndicator.Engine.Decision.Rules;
using FusionEngine = IQIAIndicator.Engine.Fusion.EvidenceFusionEngine;

namespace IQIAIndicator;

/// <summary>
/// Point d'entree de l'indicateur IQIA dans ATAS.
/// Sprint 2 : construit le MarketContext, le valide, puis appelle le RegimeEngine.
/// Aucun signal, aucune fleche, aucun calcul de risque.
/// </summary>
[DisplayName("IQIA Signal")]
[Category("IQIA")]
public sealed class IQIAIndicator : Indicator
{
    private MarketContextBuilder _builder = null!;  // initialise au premier bar

    private readonly MarketContextValidator _validator    = new();
    private readonly MarketCache            _cache        = new();
    private readonly ILogger                _logger       = NullLogger.Instance;
    private readonly RegimeEngine           _regimeEngine = new();
    private readonly DecisionEngine         _decisionEngine = new(
    [
        new DecisionRules.StableRangeRule(),
        new DecisionRules.TrendingRule(),
        new DecisionRules.MeanRevertingRule(),
        new DecisionRules.StructuralBreakRule(),
        new DecisionRules.RandomWalkRule()
    ]);
    private readonly FusionEngine           _fusion       = new(
    [
        new StationarityRule(),
        new PersistenceRule(),
        new MeanReversionRule(),
        new RandomWalkRule()
    ]);
    private readonly FusionStateManager     _fusionState  = new();
    private readonly MethodologyEngine       _methodologyEngine = new();
    private readonly SignalEngine            _signalEngine = new();
    private readonly DashboardManager        _dashboardManager = new();
    private readonly ATASRenderer            _atasRenderer = new();

    private ExecutionContext? _latestExecution;
    private EvidenceSet? _latestEvidence;
    private FusionResult? _latestFusionResult;
    private FusionSnapshot? _latestFusionSnapshot;
    private DecisionResult? _latestDecisionResult;
    private MethodologySelection? _latestMethodologySelection;
    private OpportunityPresentation? _latestOpportunityPresentation;
    private ChartAnnotationCandidate? _latestChartAnnotationCandidate;
    private ScientificAssessment? _latestScientificAssessment;
    private EntryCandidate? _latestEntryCandidate;
    private VisualizationCandidate? _latestVisualizationCandidate;
    private global::IQIAIndicator.Engine.ScientificModels.Abstractions.MarketContext? _latestScientificMarketContext;
    private bool _latestRendererCalled;
    private int _latestAnnotationsRendered;
    private DateTime? _latestRenderTime;
    private int _latestBarIndex;
    private DateTime _latestTimestamp;
    private int _availableEvidenceCount;
    private PipelineTraceRun? _latestPipelineTrace;
    private IPipelineTraceCollector? _latestPipelineTraceCollector;
    private string _latestPipelineTraceReport = string.Empty;
    private ScientificDatasetCollector? _scientificDatasetCollector;
    private ScientificDatasetSessionWriter? _scientificDatasetWriter;
    private Guid _scientificDatasetSessionId;
    private DateTime _scientificDatasetStartTime;
    private ScientificDatasetSession? _lastScientificDatasetSession;

    // --- Parametres instrument -----------------------------------------------
    [Display(Name = "Valeur du Tick (€/$)", GroupName = "Instrument", Order = 10)]
    [Range(0.01, 10_000)]
    public decimal TickValue { get; set; } = 12.50m;

    [Display(Name = "Valeur du Point (€/$)", GroupName = "Instrument", Order = 20)]
    [Range(0.01, 100_000)]
    public decimal PointValue { get; set; } = 50m;

    [Display(Name = "Decimales du prix", GroupName = "Instrument", Order = 30)]
    [Range(0, 8)]
    public int PriceDecimals { get; set; } = 2;

    [Display(Name = "Dashboard actif", GroupName = "Affichage", Order = 100)]
    public DashboardKind ActiveDashboard { get; set; } = DashboardKind.Trading;

    [Display(Name = "Activer le tracing pipeline", GroupName = "Diagnostic", Order = 120)]
    public bool EnablePipelineTracing { get; set; }

    public string LastPipelineTraceReport => _latestPipelineTraceReport;

    [Display(Name = "Activer le dataset scientifique", GroupName = "Diagnostic", Order = 130)]
    public bool EnableScientificDataset { get; set; }

    [Display(Name = "Dossier du dataset scientifique", GroupName = "Diagnostic", Order = 140)]
    public string ScientificDatasetOutputDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IQIA", "ScientificDataset");

    public ScientificDatasetSession? LastScientificDatasetSession => _lastScientificDatasetSession;

    public string LastScientificDatasetReport => _lastScientificDatasetSession?.BuildReport() ?? string.Empty;

    public string ExportScientificDataset()
    {
        if (_scientificDatasetCollector is null || _scientificDatasetCollector.Count == 0)
            return string.Empty;

        _scientificDatasetWriter ??= new ScientificDatasetSessionWriter();
        DateTime endTime = DateTime.UtcNow;
        _lastScientificDatasetSession = _scientificDatasetWriter.Export(
            _scientificDatasetCollector,
            ScientificDatasetOutputDirectory,
            _scientificDatasetCollector.Records[0].Symbol,
            _scientificDatasetCollector.Records[0].TimeFrame,
            _scientificDatasetSessionId,
            _scientificDatasetStartTime,
            endTime);
        return _lastScientificDatasetSession.BuildReport();
    }

    public IQIAIndicator() : base(true)
    {
        DenyToChangePanel = true;
        ((ValueDataSeries)DataSeries[0]).VisualType = VisualMode.Hide;
        EnableCustomDrawing = true;
        SubscribeToDrawingEvents(DrawingLayouts.Final);
    }

    protected override void OnCalculate(int bar, decimal value)
    {
        if (bar == 0 || _builder is null)
            _builder = CreateBuilder();

        if (EnableScientificDataset && _scientificDatasetCollector is null)
        {
            _scientificDatasetSessionId = Guid.NewGuid();
            _scientificDatasetCollector = new ScientificDatasetCollector(_scientificDatasetSessionId);
            _scientificDatasetStartTime = DateTime.UtcNow;
        }

        PipelineTraceRun? trace = null;
        IPipelineTraceCollector? traceCollector = null;
        if (EnablePipelineTracing)
        {
            traceCollector = new PipelineTraceCollector();
            trace = traceCollector.StartRun(
                Guid.NewGuid(),
                DateTime.UtcNow,
                bar,
                InstrumentInfo?.Instrument ?? string.Empty,
                ChartInfo?.TimeFrame ?? string.Empty);
        }

        PipelineTraceScope marketContextTrace = trace is null
            ? default
            : traceCollector!.BeginStage(trace, PipelineTraceStage.MarketContext);
        Core.MarketContext context;
        try
        {
            context = _builder.Build(bar, CurrentBar);
            var validation = _validator.Validate(context);
            if (trace is not null)
            {
                marketContextTrace.Complete(
                    PipelineTraceDetails.Create(
                        ("Timestamp", context.Clock.CurrentTime),
                        ("Symbol", context.Instrument.Symbol),
                        ("TimeFrame", context.TimeFrame),
                        ("CurrentPrice", context.Price.Close),
                        ("HistoryLength", bar + 1),
                        ("Valid", validation.IsValid)),
                    validation.Errors.Count);
            }

            if (!validation.IsValid)
            {
                _latestPipelineTrace = trace;
                _latestPipelineTraceCollector = traceCollector;
                _latestPipelineTraceReport = trace is null ? string.Empty : traceCollector!.BuildReport(trace);
                return;
            }
        }
        catch (Exception exception)
        {
            marketContextTrace.Fail(exception);
            throw;
        }

        _latestExecution = context.Execution;
        var evidence = _regimeEngine.Collect(context);
    _latestEvidence = evidence;
        _latestFusionResult = _fusion.Fuse(
            new FusionContext
            {
                Evidence = evidence,
                Timestamp = evidence.Timestamp,
                Symbol = context.Instrument.Symbol,
                TimeFrame = context.TimeFrame,
                EvaluationId = Guid.NewGuid()
            });
        _latestFusionSnapshot = _fusionState.Update(_latestFusionResult, evidence.Timestamp);
        PipelineTraceScope decisionTrace = trace is null ? default : traceCollector!.BeginStage(trace, PipelineTraceStage.Decision);
        try
        {
            _latestDecisionResult = _decisionEngine.Evaluate(
                new DecisionContext
                {
                    FusionResult = _latestFusionSnapshot.StableResult,
                    Evidence = evidence
                });
            if (trace is not null)
            {
                decisionTrace.Complete(
                    PipelineTraceDetails.Create(
                        ("ScientificScore", _latestDecisionResult.Candidates.Length == 0 ? 0.0 : _latestDecisionResult.Candidates[0].ScientificScore),
                        ("QualityScore", _latestDecisionResult.Candidates.Length == 0 ? 0.0 : _latestDecisionResult.Candidates[0].QualityScore),
                        ("FinalScore", _latestDecisionResult.WinnerScore),
                        ("TriggeredRules", string.Join(", ", _latestDecisionResult.TriggeredRules)),
                        ("RejectedRules", string.Join(", ", _latestDecisionResult.RejectedRules)),
                        ("RuleExplanation", _latestDecisionResult.RuleExplanation),
                        ("ArbitrationExplanation", _latestDecisionResult.ArbitrationExplanation)),
                    _latestDecisionResult.TriggeredRules.Count + _latestDecisionResult.RejectedRules.Count);
            }
        }
        catch (Exception exception)
        {
            decisionTrace.Fail(exception);
            throw;
        }

        PipelineTraceScope methodologyTrace = trace is null ? default : traceCollector!.BeginStage(trace, PipelineTraceStage.Methodology);
        try
        {
            _latestMethodologySelection = _methodologyEngine.Evaluate(_latestDecisionResult);
            if (trace is not null)
            {
                methodologyTrace.Complete(
                    PipelineTraceDetails.Create(("Methodology", _latestMethodologySelection.SelectedMethodology.Name)));
            }
        }
        catch (Exception exception)
        {
            methodologyTrace.Fail(exception);
            throw;
        }
        _latestScientificMarketContext = CreateScientificMarketContext(context, bar);
        _latestOpportunityPresentation = _signalEngine.Process(_latestScientificMarketContext, _latestMethodologySelection, trace);
        _latestChartAnnotationCandidate = _signalEngine.LastChartAnnotationCandidate;
        _latestScientificAssessment = _signalEngine.LastScientificAssessment;
        _latestEntryCandidate = _signalEngine.LastEntryCandidate;
        _latestVisualizationCandidate = _signalEngine.LastVisualizationCandidate;
        if (ActiveDashboard == DashboardKind.Debug)
        {
            LogPipelineDebug(context, _latestDecisionResult, _latestMethodologySelection, _latestOpportunityPresentation, _latestChartAnnotationCandidate);
        }
        _latestBarIndex = bar;
        _latestTimestamp = evidence.Timestamp;
        _availableEvidenceCount = CountAvailableEvidence(evidence);
        _latestPipelineTrace = trace;
        _latestPipelineTraceCollector = traceCollector;
        _latestPipelineTraceReport = trace is null ? string.Empty : traceCollector!.BuildReport(trace);

        if (EnableScientificDataset)
        {
            _scientificDatasetCollector!.Add(ScientificDatasetRecord.From(
                _scientificDatasetSessionId,
                bar,
                _latestScientificMarketContext,
                _latestScientificAssessment!,
                _latestDecisionResult,
                trace));
        }
    }

    /// <summary>
    /// Sprint 13.5 (H2) : point d'entrée réel du clic souris, validé en runtime au Sprint 13.4
    /// (ProcessMouseClick reçu, coordonnées X/Y directement compatibles avec l'espace utilisé par
    /// RenderContext dans OnRender — cf. rapport 13.4). Toute la résolution (quel Dashboard, quelle
    /// carte, header ou contenu) reste dans DashboardManager/ScientificDashboard : IQIAIndicator ne
    /// fait que transmettre l'événement et déclencher un redraw si le clic a été consommé.
    /// Chaque clic (y compris ceux d'un double-clic, non traité séparément — Étape 15) déclenche un
    /// toggle indépendant et déterministe : pas de debounce, pas de distinction simple/double-clic.
    /// </summary>
    public override bool ProcessMouseClick(RenderControlMouseEventArgs e)
    {
        bool handled = _dashboardManager.TryHandleMouseClick(e.X, e.Y, ActiveDashboard);
        if (handled)
        {
            e.Handled = true;
            RedrawChart(new RedrawArg(ChartArea));
            return true;
        }

        return base.ProcessMouseClick(e);
    }

    protected override void OnRender(RenderContext renderContext, DrawingLayouts layout)
    {
        base.OnRender(renderContext, layout);

        if (layout == DrawingLayouts.Final && _latestEvidence is not null && _latestFusionResult is not null &&
            _latestFusionSnapshot is not null &&
            _latestDecisionResult is not null)
        {
            if (_latestChartAnnotationCandidate is not null)
            {
                PipelineTraceScope rendererTrace = _latestPipelineTrace is null
                    ? default
                    : _latestPipelineTraceCollector!.BeginStage(_latestPipelineTrace, PipelineTraceStage.Renderer);
                _latestRendererCalled = true;
                _latestAnnotationsRendered = _latestChartAnnotationCandidate.Annotations?.Count ?? 0;
                _latestRenderTime = DateTime.UtcNow;
                try
                {
                    _atasRenderer.Render(renderContext, _latestChartAnnotationCandidate);
                    IReadOnlyDictionary<string, string> rendererDetails = _atasRenderer.Describe(_latestChartAnnotationCandidate);
                    if (_latestPipelineTrace is not null)
                    {
                        rendererTrace.Complete(
                            PipelineTraceDetails.Create(
                                ("AnnotationCreated", true),
                                ("AnnotationCount", _latestAnnotationsRendered),
                                ("Position", rendererDetails["Position"]),
                                ("Color", rendererDetails["Color"]),
                                ("Text", rendererDetails["Text"]),
                                ("RenderSuccess", true)),
                            _latestChartAnnotationCandidate.Diagnostics?.Count ?? 0);
                    }
                }
                catch (Exception exception)
                {
                    rendererTrace.Fail(exception);
                    throw;
                }
            }
            else
            {
                PipelineTraceScope rendererTrace = _latestPipelineTrace is null
                    ? default
                    : _latestPipelineTraceCollector!.BeginStage(_latestPipelineTrace, PipelineTraceStage.Renderer);
                _latestRendererCalled = false;
                _latestAnnotationsRendered = 0;
                _latestRenderTime = null;
                if (_latestPipelineTrace is not null)
                {
                    rendererTrace.Complete(PipelineTraceDetails.Create(
                        ("AnnotationCreated", false),
                        ("AnnotationCount", 0),
                        ("RenderSuccess", false)));
                }
            }

            if (_latestPipelineTrace is not null)
                _latestPipelineTraceReport = _latestPipelineTraceCollector!.BuildReport(_latestPipelineTrace);

            var dashboardContext = new DashboardContext
            {
                BarIndex = _latestBarIndex,
                Timestamp = _latestTimestamp,
                AvailableEvidenceCount = _availableEvidenceCount,
                ScientificMarketContext = _latestScientificMarketContext,
                Execution = _latestExecution,
                Evidence = _latestEvidence,
                FusionResult = _latestFusionResult,
                FusionSnapshot = _latestFusionSnapshot,
                DecisionResult = _latestDecisionResult,
                MethodologySelection = _latestMethodologySelection,
                ScientificAssessment = _latestScientificAssessment,
                EntryCandidate = _latestEntryCandidate,
                EntryTriggerCandidate = _signalEngine.LastEntryTriggerCandidate,
                EntryTiming = _signalEngine.LastEntryTiming,
                VisualizationCandidate = _latestVisualizationCandidate,
                ChartAnnotationCandidate = _latestChartAnnotationCandidate,
                OpportunityPresentation = _latestOpportunityPresentation,
                RendererCalled = _latestRendererCalled,
                AnnotationsRendered = _latestAnnotationsRendered,
                LastRenderTime = _latestRenderTime,
                PipelineTrace = _latestPipelineTrace,
                PipelineTraceReport = _latestPipelineTraceReport,
                EnablePipelineTracing = EnablePipelineTracing,
                EnableScientificDataset = EnableScientificDataset,
                DatasetCollector = _scientificDatasetCollector,
                DatasetSession = _lastScientificDatasetSession,
                DatasetOutputDirectory = ScientificDatasetOutputDirectory,
                DatasetStartTime = _scientificDatasetCollector is null ? null : _scientificDatasetStartTime
            };

            _dashboardManager.Draw(renderContext, dashboardContext, ActiveDashboard, ChartArea.Width);
        }
    }

    private static int CountAvailableEvidence(EvidenceSet evidence)
    {
        int count = 0;
        count += evidence.Adf is { IsValid: true } ? 1 : 0;
        count += evidence.Kpss is { IsValid: true } ? 1 : 0;
        count += evidence.Hurst is { IsValid: true } ? 1 : 0;
        count += evidence.HalfLife is { IsValid: true } ? 1 : 0;
        count += evidence.VarianceRatio is { IsValid: true } ? 1 : 0;
        count += evidence.Cusum is { IsValid: true } ? 1 : 0;
        count += evidence.Volatility is { IsValid: true } ? 1 : 0;
        count += evidence.BaiPerron is { IsValid: true } ? 1 : 0;
        count += evidence.Dfa is { IsValid: true } ? 1 : 0;
        return count;
    }

    private void LogPipelineDebug(
        MarketContext context,
        DecisionResult decisionResult,
        MethodologySelection methodologySelection,
        OpportunityPresentation? opportunityPresentation,
        ChartAnnotationCandidate? chartAnnotationCandidate)
    {
        _logger.Info("========== IQIA DEBUG ==========");
        _logger.Info($"Time : {DateTime.UtcNow:O}");
        _logger.Info($"CurrentPrice : {context.Price.Close}");
        _logger.Info($"CurrentBar : {context.Execution.CurrentBar}");
        _logger.Info($"History Count : {(context.Price.Close == 0m ? 0 : 1)}");
        _logger.Info($"History First : {context.Price.Close}");
        _logger.Info($"History Last : {context.Price.Close}");
        _logger.Info($"Symbol : {context.Instrument.Symbol}");
        _logger.Info($"TimeFrame : {context.TimeFrame}");
        _logger.Info($"MarketState : {decisionResult.Winner}");
        _logger.Info($"Timestamp : {context.Clock.CurrentTime:O}");

        if (context.Price.Close <= 0m || context.Instrument.Symbol.Length == 0 || context.Clock.CurrentTime == default)
        {
            _logger.Warning("******** WARNING ********");
            _logger.Warning("MarketContext incomplet");
            _logger.Warning("***********************");
        }

        _logger.Info("========== SIGNAL ENGINE ==========");
        _logger.Info($"Methodology : {methodologySelection.SelectedMethodology.Name}");
        _logger.Info($"Scientific Results : {(_latestOpportunityPresentation is null ? "N/A" : "available")}");
        _logger.Info($"Scientific Success : {(_latestOpportunityPresentation is null ? "N/A" : "n/a")}");
        _logger.Info($"Scientific Failure : {(_latestOpportunityPresentation is null ? "N/A" : "n/a")}");
        _logger.Info($"Entry Status : {(opportunityPresentation is null ? "N/A" : opportunityPresentation.OpportunityStatus)}");
        _logger.Info($"Opportunity Status : {(opportunityPresentation is null ? "N/A" : opportunityPresentation.OpportunityStatus)}");
        _logger.Info($"Visualization : {(chartAnnotationCandidate is null ? "N/A" : "available")}");
        _logger.Info($"Presentation : {(opportunityPresentation is null ? "N/A" : "available")}");

        if (chartAnnotationCandidate is not null)
        {
            _logger.Info($"ChartAnnotationCandidate Annotations : {(chartAnnotationCandidate.Annotations?.Count ?? 0)}");
            _logger.Info($"Arrow Up : {(chartAnnotationCandidate.Annotations?.Count(annotation => annotation.AnnotationType == AnnotationType.Arrow && annotation.Visibility == AnnotationVisibility.Pinned) ?? 0)}");
            _logger.Info($"Arrow Down : {(chartAnnotationCandidate.Annotations?.Count(annotation => annotation.AnnotationType == AnnotationType.Arrow && annotation.Visibility == AnnotationVisibility.Hidden) ?? 0)}");
            _logger.Info($"Labels : {(chartAnnotationCandidate.Annotations?.Count(annotation => annotation.AnnotationType == AnnotationType.Label) ?? 0)}");
            _logger.Info($"Warnings : {(chartAnnotationCandidate.Warnings?.Count ?? 0)}");
        }

        _logger.Info("===============================");
    }

    // --- Cablage du builder avec les sources ATAS ----------------------------

    private global::IQIAIndicator.Engine.ScientificModels.Abstractions.MarketContext CreateScientificMarketContext(MarketContext context, int bar)
    {
        const int historyWindow = 500;
        int startIndex = Math.Max(0, bar - historyWindow + 1);
        var history = new List<decimal>(Math.Min(historyWindow, bar + 1));

        for (int index = startIndex; index <= bar; index++)
        {
            history.Add(GetCandle(index).Close);
        }

        return new global::IQIAIndicator.Engine.ScientificModels.Abstractions.MarketContext(
            context.Clock.CurrentTime,
            context.Execution.CurrentBar,
            history.AsReadOnly(),
            context.Price.Close,
            context.Instrument.Symbol,
            context.TimeFrame,
            null,
            context.Clock.Session.Name);
    }

    private MarketContextBuilder CreateBuilder() =>
        new(
            getBar:     GetCandle,
            symbol:     InstrumentInfo?.Instrument ?? string.Empty,
            tickSize:   InstrumentInfo?.TickSize   ?? 0m,
            tickValue:  TickValue,
            pointValue: PointValue,
            decimals:   PriceDecimals,
            timeFrame:  ChartInfo?.TimeFrame       ?? string.Empty
        );
}
