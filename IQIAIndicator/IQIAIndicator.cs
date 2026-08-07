using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ATAS.Indicators;
using IQIAIndicator.Core;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Fusion;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Fusion.State;
using IQIAIndicator.Engine.Regime;
using IQIAIndicator.Engine.Regime.Core;
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
        new StructuralStabilityRule(),
        new RandomWalkRule()
    ]);
    private readonly FusionStateManager     _fusionState  = new();
    private readonly IQIAFusionDashboard _dashboard = new();

    private EvidenceSet? _latestEvidence;
    private FusionResult? _latestFusionResult;
    private FusionSnapshot? _latestFusionSnapshot;
    private DecisionResult? _latestDecisionResult;
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

    // --- Cablage du builder avec les sources ATAS ----------------------------

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
