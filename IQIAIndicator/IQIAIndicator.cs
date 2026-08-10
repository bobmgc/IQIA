using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using ATAS.Indicators;
using IQIAIndicator.Core;
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
using IQIAIndicator.Visualization;
using OFT.Rendering.Context;
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
    private readonly IQIAFusionDashboard     _dashboard = new();
    private readonly IQIAPipelineDashboard   _pipelineDashboard = new();
    private readonly ATASRenderer            _atasRenderer = new();

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

    [Display(Name = "Mode Debug", GroupName = "Diagnostic", Order = 100)]
    public bool DebugMode { get; set; }

    [Display(Name = "Debug Pipeline", GroupName = "Diagnostic", Order = 110)]
    public bool DebugPipeline { get; set; }

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

        var context    = _builder.Build(bar, CurrentBar);
        var validation = _validator.Validate(context);

        if (!validation.IsValid)
            return;

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
        _latestDecisionResult = _decisionEngine.Evaluate(
            new DecisionContext
            {
                FusionResult = _latestFusionSnapshot.StableResult,
                Evidence = evidence
            });
        _latestMethodologySelection = _methodologyEngine.Evaluate(_latestDecisionResult);
        _latestScientificMarketContext = CreateScientificMarketContext(context, bar);
        _latestOpportunityPresentation = _signalEngine.Process(_latestScientificMarketContext, _latestMethodologySelection);
        _latestChartAnnotationCandidate = _signalEngine.LastChartAnnotationCandidate;
        _latestScientificAssessment = _signalEngine.LastScientificAssessment;
        _latestEntryCandidate = _signalEngine.LastEntryCandidate;
        _latestVisualizationCandidate = _signalEngine.LastVisualizationCandidate;
        if (DebugPipeline)
        {
            LogPipelineDebug(context, _latestDecisionResult, _latestMethodologySelection, _latestOpportunityPresentation, _latestChartAnnotationCandidate);
        }
        _latestBarIndex = bar;
        _latestTimestamp = evidence.Timestamp;
        _availableEvidenceCount = CountAvailableEvidence(evidence);
    }

    protected override void OnRender(RenderContext renderContext, DrawingLayouts layout)
    {
        base.OnRender(renderContext, layout);

        if (layout == DrawingLayouts.Final && _latestEvidence is not null && _latestFusionResult is not null &&
            _latestFusionSnapshot is not null &&
            _latestDecisionResult is not null)
        {
            _dashboard.Draw(
                renderContext,
                _latestEvidence,
                _latestFusionResult,
                _latestFusionSnapshot,
                _latestDecisionResult,
                _latestBarIndex,
                _latestTimestamp,
                _availableEvidenceCount,
                DebugMode);

            if (DebugPipeline)
            {
                _pipelineDashboard.Draw(
                    renderContext,
                    _latestScientificMarketContext,
                    _latestEvidence,
                    _latestFusionResult,
                    _latestFusionSnapshot,
                    _latestDecisionResult,
                    _latestMethodologySelection,
                    _latestOpportunityPresentation,
                    _latestChartAnnotationCandidate,
                    _latestScientificAssessment,
                    _latestEntryCandidate,
                    _latestVisualizationCandidate,
                    _latestRendererCalled,
                    _latestAnnotationsRendered,
                    _latestRenderTime,
                    _latestBarIndex,
                    _latestTimestamp,
                    _availableEvidenceCount);
            }

            if (_latestChartAnnotationCandidate is not null)
            {
                _latestRendererCalled = true;
                _latestAnnotationsRendered = _latestChartAnnotationCandidate.Annotations?.Count ?? 0;
                _latestRenderTime = DateTime.UtcNow;
                _atasRenderer.Render(renderContext, _latestChartAnnotationCandidate);
            }
            else
            {
                _latestRendererCalled = false;
                _latestAnnotationsRendered = 0;
                _latestRenderTime = null;
            }
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
