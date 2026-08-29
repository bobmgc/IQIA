using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using ATAS.DataFeedsCore;
using ATAS.DataFeedsCore.Statistics;
using ATAS.Indicators;
using IQIAIndicator.Core;
using IQIAIndicator.Core.Observability;
using IQIAIndicator.Core.Calibration;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Entry;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Fusion;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Fusion.State;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.Presentation;
using IQIAIndicator.Engine.Regime;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.ScientificModels.MeanReversion;
using IQIAIndicator.Engine.Signal;
using IQIAIndicator.Engine.ScientificFusion;
using IQIAIndicator.Engine.TradePlan;
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
        new RandomWalkRule(),
        // Sprint 15.25 (Lot 15.8): additive evidence dimension only - not consumed by any
        // Decision.Rules.IDecisionRule yet (see StructuralBreakEvidenceRule doc comment).
        new StructuralBreakEvidenceRule()
    ]);
    private readonly FusionStateManager     _fusionState  = new();
    private readonly MethodologyEngine       _methodologyEngine = new();
    private readonly SignalEngine            _signalEngine = new();
    private readonly TradePlanEngine         _tradePlanEngine = new();
    private readonly TradePlanAnnotationEngine _tradePlanAnnotationEngine = new();
    private readonly RiskEngine              _riskEngine = new();
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
    private TradePlan? _latestTradePlan;
    private TradePlanAnnotationCandidate? _latestTradePlanAnnotationCandidate;
    private RiskAssessment? _latestRiskAssessment;
    // Sprint 15.25 (Lot 12): retained purely for dashboard observability (Section 11) - AccountState/
    // InstrumentRiskSpecification as actually supplied to RiskEngine this bar, so Capital/Equity/
    // Instrument stay visible even on a bar with no assessable TradePlan candidate.
    private AccountState? _latestRiskAccountState;
    private InstrumentRiskSpecification? _latestRiskInstrumentSpec;
    // Sprint 15.25 (Lot 12.3): TEMPORARY DIAGNOSTIC fields - see ATASRuntimeDiagnostics.cs's doc
    // comment. Candidates for removal once the Lot 12.3 root cause is confirmed/addressed.
    private ATASAccountDiagnostic? _latestATASAccountDiagnostic;
    private ATASInstrumentDiagnostic? _latestATASInstrumentDiagnostic;
    // Sprint 15.25 (Lot 12.5 - ATAS raw risk telemetry): TEMPORARY OBSERVABILITY fields. _latestRiskEngineRequest
    // is the exact request object RiskEngine.Evaluate received this bar (or null - same gate as
    // _latestRiskAssessment), retained only so ScientificDatasetRecord.From can capture it; nothing else
    // reads it. _latestAtasEquitySeriesCount/_latestAtasEquityLastTimestamp are a second, independent,
    // passive read of the same ITradingStatistics.Equity series ATASAccountStateAdapter.TryGetCurrentEquity
    // already consults - see the capture site below for why this does not duplicate that adapter's logic.
    private RiskEngineRequest? _latestRiskEngineRequest;
    private int? _latestAtasEquitySeriesCount;
    private DateTime? _latestAtasEquityLastTimestamp;
    // Sprint 15.25 (Lot 12.6 - ATAS binding correction, extended Lot 12.11): see
    // ATASEquityReplayDetector.cs (wired in) and ATASInstrumentQuantityStepResolver.cs (investigated,
    // NOT wired in - see its doc comment) for the full rationale. _latestEquitySourceIsReplay is the
    // CORRECTED signal actually used to select Equity's source (Replay vs Realtime) this bar;
    // _latestPortfolioIsReplay (Portfolio.IsReplay(), ATAS-native) is now ALSO an input to that same
    // decision (Lot 12.11 - previously captured for observability only); the two *Source fields name
    // which source (ATAS vs manual parameter) produced the resolved MinQuantity/MaxQuantity (unchanged
    // logic - Lot 12.2, protected), for telemetry only.
    private bool _latestEquitySourceIsReplay;
    private bool? _latestPortfolioIsReplay;
    // Sprint 15.25 (Lot 12.12, Problem A/C): the resolved Live/Replay context (see AtasDataContext.cs),
    // wrapping the SAME _latestEquitySourceIsReplay decision above - never a second decision - so every
    // dashboard consumer reads the one already-corrected answer instead of the raw, independently
    // unreliable ExecutionContext.IsReplay heuristic. Stays Unknown until the Risk stage has produced a
    // resolution at least once this session.
    private AtasDataContext _latestAtasDataContext = AtasDataContext.Unknown;
    // Sprint 15.25 (Lot 12.12, Problem B): non-null only when the Risk stage's ATAS-owned reads
    // (TradingManager/.Portfolio/.Security/TradingStatisticsProvider) threw on the most recent bar - see
    // the Risk stage's catch clause below for the full rationale. Never influences any decision; purely
    // diagnostic (DEBUG dashboard, RISK ENGINE panel Reason, ScientificDatasetRecord).
    private string? _latestRiskStageError;
    private string _latestMinQuantitySource = ATASInstrumentQuantityStepResolver.ManualSource;
    private string _latestMaxQuantitySource = ATASInstrumentQuantityStepResolver.ManualSource;
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
    private ExportResult? _lastExportResult;
    private bool _scientificDatasetAutoExportAttempted;
    private readonly DatasetLifecycleLog _datasetLifecycleLog = new();
    private string? _lastLifecycleSnapshotPath;
    private string? _lastLifecycleSnapshotError;

    // Sprint 15.17.2: throttles the mid-session periodic snapshot write in OnCalculate - writing on
    // every single bar would add disk I/O to the hot ATAS calculation path; every 50 attempts is
    // frequent enough to keep the on-disk snapshot recent without measurable overhead.
    private const int LifecycleSnapshotWriteEveryNBars = 50;

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

    // --- Parametres Risk Engine (Sprint 15.25, Lot 11) ------------------------
    // Sprint 15.25 (Lot 11, Section 3/19): this is the injection point Lot 10's RiskEngine was built to
    // receive - never a fabricated capital/policy value. Every field below defaults to 0 (or 0.0), which
    // RiskEngine already treats as "not configured": InitialCapital/CurrentEquity=0 -> INVALID_CAPITAL/
    // INVALID_EQUITY (see RiskEngine.cs Phase 1); MinQuantity/MaxQuantity/QuantityStep=0 ->
    // INSTRUMENT_SPEC_INVALID (InstrumentRiskSpecification.IsValid); every RiskPolicy* field=0 ->
    // unconstrained (RiskPolicyFactory.PositiveOrNull/PositiveFractionOrNull). Nothing here changes
    // trading behaviour: this indicator never sends an order regardless of RiskAssessment (Lot 11,
    // Section 11 - strictly observational). The four "(%)" fields are WHOLE percents as typed in this
    // property grid (2 = 2%) - RiskPolicyFactory converts to the 0.02 fraction RiskPolicy stores.
    [Display(Name = "Capital initial", GroupName = "Risk Engine", Order = 200)]
    [Range(0, 100_000_000)]
    public decimal RiskInitialCapital { get; set; } = 0m;

    /// <summary>Sprint 15.25 (Lot 12.2): no longer read by the Risk stage. CurrentEquity now comes
    /// exclusively from ATAS (Indicator.TradingStatisticsProvider - see ATASAccountStateAdapter), with
    /// unavailable equity failing closed (INVALID_EQUITY) rather than ever falling back to this manual
    /// value (Lot 12.2, Section 5 - a stale/manual guess masking a real ATAS outage would be worse than
    /// an honest rejection). Kept as a declared property only so a previously-saved ATAS indicator
    /// configuration referencing it does not silently break; it has no effect on the Risk Engine.</summary>
    [Display(Name = "Equity actuelle (non utilisé depuis Lot 12.2 - voir ATAS)", GroupName = "Risk Engine", Order = 205)]
    [Range(0, 100_000_000)]
    public decimal RiskCurrentEquity { get; set; } = 0m;

    /// <summary>Sprint 15.25 (Lot 12.2): no longer read by the Risk stage. CurrentBalance now comes from
    /// ATAS (Indicator.TradingManager.Portfolio.Balance) when available, null otherwise - see
    /// ATASAccountStateAdapter.Build. Kept as a declared property only so a previously-saved ATAS
    /// indicator configuration referencing it does not silently break; it has no effect on the Risk
    /// Engine (CurrentBalance itself is informational only - unused by RiskEngine.Evaluate, Lot 10).</summary>
    [Display(Name = "Solde actuel (non utilisé depuis Lot 12.2 - voir ATAS)", GroupName = "Risk Engine", Order = 210)]
    [Range(0, 100_000_000)]
    public decimal RiskCurrentBalance { get; set; } = 0m;

    [Display(Name = "Peak equity", GroupName = "Risk Engine", Order = 215)]
    [Range(0, 100_000_000)]
    public decimal RiskPeakEquity { get; set; } = 0m;

    [Display(Name = "Equity de debut de journee", GroupName = "Risk Engine", Order = 220)]
    [Range(0, 100_000_000)]
    public decimal RiskDailyStartingEquity { get; set; } = 0m;

    [Display(Name = "PnL du jour", GroupName = "Risk Engine", Order = 225)]
    [Range(-100_000_000, 100_000_000)]
    public decimal RiskDailyPnL { get; set; } = 0m;

    [Display(Name = "Risque deja utilise aujourd'hui", GroupName = "Risk Engine", Order = 230)]
    [Range(0, 100_000_000)]
    public decimal RiskUsedToday { get; set; } = 0m;

    [Display(Name = "Risque ouvert actuel", GroupName = "Risk Engine", Order = 235)]
    [Range(0, 100_000_000)]
    public decimal RiskOpenRisk { get; set; } = 0m;

    [Display(Name = "Risque max par trade (%)", GroupName = "Risk Engine", Order = 240)]
    [Range(0, 100)]
    public decimal RiskPolicyMaxRiskPerTradePercent { get; set; } = 0m;

    [Display(Name = "Risque max par trade (montant)", GroupName = "Risk Engine", Order = 245)]
    [Range(0, 100_000_000)]
    public decimal RiskPolicyMaxRiskPerTradeAmount { get; set; } = 0m;

    [Display(Name = "Perte max journaliere (%)", GroupName = "Risk Engine", Order = 250)]
    [Range(0, 100)]
    public decimal RiskPolicyMaxDailyLossPercent { get; set; } = 0m;

    [Display(Name = "Perte max journaliere (montant)", GroupName = "Risk Engine", Order = 255)]
    [Range(0, 100_000_000)]
    public decimal RiskPolicyMaxDailyLossAmount { get; set; } = 0m;

    [Display(Name = "Drawdown max (%)", GroupName = "Risk Engine", Order = 260)]
    [Range(0, 100)]
    public decimal RiskPolicyMaxDrawdownPercent { get; set; } = 0m;

    [Display(Name = "Drawdown max (montant)", GroupName = "Risk Engine", Order = 265)]
    [Range(0, 100_000_000)]
    public decimal RiskPolicyMaxDrawdownAmount { get; set; } = 0m;

    [Display(Name = "Risque ouvert max (%)", GroupName = "Risk Engine", Order = 270)]
    [Range(0, 100)]
    public decimal RiskPolicyMaxOpenRiskPercent { get; set; } = 0m;

    [Display(Name = "Risque ouvert max (montant)", GroupName = "Risk Engine", Order = 275)]
    [Range(0, 100_000_000)]
    public decimal RiskPolicyMaxOpenRiskAmount { get; set; } = 0m;

    [Display(Name = "Risk/Reward minimum", GroupName = "Risk Engine", Order = 280)]
    [Range(0, 100)]
    public double RiskPolicyMinRiskReward { get; set; } = 0.0;

    [Display(Name = "Taille de position max (policy)", GroupName = "Risk Engine", Order = 285)]
    [Range(0, 100_000)]
    public int RiskPolicyMaxPositionSize { get; set; } = 0;

    /// <summary>Sprint 15.25 (Lot 12.2): fallback only. ATASInstrumentAdapter prefers ATAS's own
    /// Security.LotMinSize when it reports a positive value; this manual parameter is used only when
    /// ATAS does not provide one - never silently overridden when ATAS does.</summary>
    [Display(Name = "Quantite minimum (fallback si ATAS absent)", GroupName = "Risk Engine", Order = 290)]
    [Range(0, 100_000)]
    public int RiskInstrumentMinQuantity { get; set; } = 0;

    /// <summary>Sprint 15.25 (Lot 12.2): fallback only - see RiskInstrumentMinQuantity's doc comment
    /// (same rule, for Security.LotMaxSize).</summary>
    [Display(Name = "Quantite maximum (fallback si ATAS absent)", GroupName = "Risk Engine", Order = 295)]
    [Range(0, 100_000)]
    public int RiskInstrumentMaxQuantity { get; set; } = 0;

    /// <summary>Sprint 15.25 (Lot 12.2): still the only source - Lot 12.1 found no ATAS equivalent for a
    /// quantity step/increment, and none is fabricated here (see ATASInstrumentAdapter's doc comment).</summary>
    [Display(Name = "Pas de quantite", GroupName = "Risk Engine", Order = 300)]
    [Range(0, 100_000)]
    public int RiskInstrumentQuantityStep { get; set; } = 0;

    /// <summary>Sprint 15.25 (Lot 11): the most recent RiskAssessment, for observability (tests,
    /// diagnostics, ScientificDataset) - null whenever RiskEngineRequestFactory could not build a
    /// request (no assessable TradePlan candidate this bar). Never influences the pipeline itself.</summary>
    public RiskAssessment? LastRiskAssessment => _latestRiskAssessment;

    /// <summary>Sprint 15.25 (Lot 12.3): TEMPORARY DIAGNOSTIC - see ATASRuntimeDiagnostics.cs's doc
    /// comment. Raw ATAS values behind this bar's AccountState/InstrumentRiskSpecification, for
    /// programmatic inspection while diagnosing why CurrentEquity/the instrument spec arrive invalid.</summary>
    public ATASAccountDiagnostic? LastATASAccountDiagnostic => _latestATASAccountDiagnostic;

    public ATASInstrumentDiagnostic? LastATASInstrumentDiagnostic => _latestATASInstrumentDiagnostic;

    /// <summary>Sprint 15.25 (Lot 12.5): the exact RiskEngineRequest RiskEngine.Evaluate received this
    /// bar - null under the same condition LastRiskAssessment is null (no assessable TradePlan
    /// candidate). Observability only, mirrors LastRiskAssessment's own doc comment.</summary>
    public RiskEngineRequest? LastRiskEngineRequest => _latestRiskEngineRequest;

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

    /// <summary>Sprint 15.23.1 (QDE-012 ATAS N=20 integration trace audit): activable, off by default.
    /// Mirrors <see cref="KalmanFilterModel.DiagnosticsEnabled"/> into the static flag that class reads
    /// - see its doc comment for why this can never change any Metrics/Score/decision. Captures at
    /// most the first 10 eligible KalmanFilterModel.Evaluate() calls of the session and writes them to
    /// "..._kalman_window_diagnostic.json" next to the scientific dataset export on OnDispose().</summary>
    [Display(Name = "Activer le diagnostic fenêtre Kalman (N=20)", GroupName = "Diagnostic", Order = 150)]
    public bool EnableKalmanWindowDiagnostic { get; set; }

    public string? LastKalmanWindowDiagnosticPath { get; private set; }

    public ScientificDatasetSession? LastScientificDatasetSession => _lastScientificDatasetSession;

    public string LastScientificDatasetReport => _lastScientificDatasetSession?.BuildReport() ?? string.Empty;

    public ExportResult? LastExportResult => _lastExportResult;

    public DatasetLifecycleLog DatasetLifecycleLog => _datasetLifecycleLog;

    /// <summary>Real, live path of the persistent lifecycle snapshot for the current session, or
    /// null before EnableScientificDataset has ever created a collector. See WriteLifecycleSnapshot.</summary>
    public string? LastLifecycleSnapshotPath => _lastLifecycleSnapshotPath;

    /// <summary>Non-null only if the most recent attempt to persist the lifecycle snapshot itself
    /// failed (e.g. disk full, permissions) - kept per Sprint 15.17.2 Phase 5's requirement to never
    /// lose this even though the write failure itself must never break the indicator.</summary>
    public string? LastLifecycleSnapshotError => _lastLifecycleSnapshotError;

    /// <summary>
    /// Sprint 15.17.1: thin wrapper around ScientificDatasetExporter.Export (the actually-testable,
    /// ATAS-independent piece) that also updates DatasetLifecycleLog and the indicator's own cached
    /// fields for backward compatibility (LastScientificDatasetSession/LastScientificDatasetReport).
    /// Never throws - every outcome (Success/NoData/Failed) is captured in the returned ExportResult
    /// and cached in _lastExportResult, so a caller (OnDispose, or a future manual trigger) never has
    /// to guess whether "nothing happened" meant no data, a swallowed exception, or genuine success -
    /// this is exactly the ambiguity Sprint 15.17.1 was opened to close.
    /// </summary>
    public ExportResult ExportScientificDataset()
    {
        _scientificDatasetWriter ??= new ScientificDatasetSessionWriter();
        _datasetLifecycleLog.MarkExportStarted(DateTime.UtcNow);
        WriteLifecycleSnapshot();

        ExportResult result = ScientificDatasetExporter.Export(
            _scientificDatasetCollector,
            _scientificDatasetWriter,
            ScientificDatasetOutputDirectory,
            _scientificDatasetSessionId,
            _scientificDatasetStartTime,
            DateTime.UtcNow);

        _lastExportResult = result;
        if (result.Session is not null)
            _lastScientificDatasetSession = result.Session;

        switch (result.Outcome)
        {
            case ExportOutcome.Success:
                _datasetLifecycleLog.MarkExportCompleted(DateTime.UtcNow);
                break;
            case ExportOutcome.Failed:
                _datasetLifecycleLog.MarkExportFailed(DateTime.UtcNow);
                break;
        }

        // Final state (Completed/Failed/still-NoData) captured on disk even if something goes wrong
        // immediately after this call returns (e.g. ATAS tears the process down right after Dispose).
        WriteLifecycleSnapshot();

        return result;
    }

    /// <summary>
    /// Sprint 15.17.2 (QDE-012 real-market capture - persistent ATAS lifecycle diagnostics). Persists
    /// DatasetLifecycleLog's current state to "*_lifecycle.json" next to the eventual export files
    /// (DatasetLifecycleSnapshotWriter, itself never-throwing). Called: once when the collector is
    /// first created (so the file exists from session start, per Phase 2 - "AVANT que l'indicateur
    /// soit détaché"), periodically while bars arrive (throttled, see
    /// LifecycleSnapshotWriteEveryNBars), and unconditionally at the very start of OnDispose() BEFORE
    /// any export is attempted - so proof that OnDispose fired survives on disk even if export itself
    /// never completes.
    ///
    /// No-op (does nothing, throws nothing) if no collector exists yet - there being nothing
    /// meaningful to persist before EnableScientificDataset has actually created a session.
    /// </summary>
    private void WriteLifecycleSnapshot()
    {
        if (_scientificDatasetCollector is null)
            return;

        string symbol = InstrumentInfo?.Instrument ?? string.Empty;
        string timeFrame = ChartInfo?.TimeFrame ?? string.Empty;

        bool success = DatasetLifecycleSnapshotWriter.TryWrite(
            ScientificDatasetOutputDirectory,
            _scientificDatasetSessionId,
            symbol,
            timeFrame,
            _scientificDatasetStartTime,
            _datasetLifecycleLog,
            _scientificDatasetCollector.TotalAddAttempts,
            _scientificDatasetCollector.RecordsAccepted,
            _lastExportResult?.ErrorMessage,
            out string path,
            out string? errorMessage);

        _lastLifecycleSnapshotPath = path;
        _lastLifecycleSnapshotError = success ? null : errorMessage;
    }

    public IQIAIndicator() : base(true)
    {
        DenyToChangePanel = true;
        ((ValueDataSeries)DataSeries[0]).VisualType = VisualMode.Hide;
        EnableCustomDrawing = true;
        SubscribeToDrawingEvents(DrawingLayouts.Final);
        _datasetLifecycleLog.MarkConstructed(DateTime.UtcNow);
    }

    protected override void OnCalculate(int bar, decimal value)
    {
        if (bar == 0 || _builder is null)
        {
            _builder = CreateBuilder();
        }
        else if (_builder.TickSize <= 0m && InstrumentInfo?.TickSize is decimal freshTickSize && freshTickSize > 0m)
        {
            // Sprint 15.25 (Lot 12.12, Problem B): self-heals a builder constructed at bar 0 before ATAS
            // had actually populated InstrumentInfo.TickSize - see MarketContextBuilder.RefreshInstrument's
            // doc comment. Deliberately does NOT go through CreateBuilder()/replace _builder: that would
            // reset _firstBarTime/_maxRealtimeBar (the Replay-heuristic bookkeeping), which this fix must
            // not disturb - only the instrument snapshot is stale here, nothing else.
            _builder.RefreshInstrument(
                InstrumentInfo?.Instrument ?? string.Empty,
                freshTickSize,
                TickValue,
                PointValue,
                PriceDecimals,
                ChartInfo?.TimeFrame ?? string.Empty);
        }

        // Sprint 15.23.1: cheap, idempotent sync of the diagnostic toggle - see
        // EnableKalmanWindowDiagnostic's doc comment. Does not alter any Kalman calculation.
        KalmanFilterModel.DiagnosticsEnabled = EnableKalmanWindowDiagnostic;

        if (EnableScientificDataset && _scientificDatasetCollector is null)
        {
            _scientificDatasetSessionId = Guid.NewGuid();
            _scientificDatasetCollector = new ScientificDatasetCollector(_scientificDatasetSessionId);
            _scientificDatasetStartTime = DateTime.UtcNow;
            _datasetLifecycleLog.MarkDatasetEnabled(_scientificDatasetStartTime);
            // Sprint 15.17.2: the lifecycle file must exist from session start, before any bar has
            // even been collected - so it survives on disk even if the indicator is torn down before
            // a single Add() succeeds.
            WriteLifecycleSnapshot();
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

        Core.MarketContext? validatedContext = BuildValidatedMarketContext(bar, trace, traceCollector);
        if (validatedContext is null)
            return;
        Core.MarketContext context = validatedContext;

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

        PipelineTraceScope tradePlanTrace = trace is null ? default : traceCollector!.BeginStage(trace, PipelineTraceStage.TradePlan);
        try
        {
            EntryTriggerCandidate? entryTriggerCandidate = _signalEngine.LastEntryTriggerCandidate;
            if (entryTriggerCandidate is not null)
            {
                var instrumentInfo = new Core.InstrumentInfo(
                    InstrumentInfo?.Instrument ?? string.Empty,
                    InstrumentInfo?.TickSize ?? 0m,
                    TickValue,
                    PointValue,
                    PriceDecimals);

                // Audit 2026-08-29 (P0-1): resolve TradeRiskParameters for a directional candidate so the
                // live/replay TradePlan can reach PLAN_READY (and RunRiskStage below can then evaluate it)
                // instead of always capping at SIGNAL_ONLY. Mirrors BacktestEngine.RunSignalPipeline's Lot
                // 15.3 wiring exactly:
                //  - StopLoss from VolatilityStopLossModel, reading VolatilityModel.CurrentVolatility as
                //    already computed for this bar (never a new evidence calculation); non-calibrated 2.0
                //    multiplier (Lot 15.3, revisited by a future calibration lot). Resolved ONLY for a
                //    BUY/SELL direction - never for NO_ACTION/WATCH, and structurally impossible outside
                //    MeanReverting (Lot 15.1), so this never fabricates a stop for an unsupported regime.
                //  - RiskPerTrade = RiskInitialCapital x the MaxRiskPerTradePercent fraction (same
                //    whole-percent -> fraction convention as RiskPolicyFactory.PositiveFractionOrNull and
                //    as BacktestEngine's scenario.Policy.MaxRiskPerTradePercent). Null when the two
                //    "Risk Engine" parameters are not configured - the TradePlan then stays honestly
                //    SIGNAL_ONLY, exactly as before this change.
                // TradePlanBuilder / RunRiskStage / RiskEngine are unchanged: they simply consume this
                // non-null RiskParameters for the first time in the live path.
                TradeRiskParameters? riskParameters = null;
                if (entryTriggerCandidate.Assessment.Direction is DirectionCandidate.BUY_CANDIDATE
                    or DirectionCandidate.SELL_CANDIDATE)
                {
                    decimal? stopLoss = VolatilityStopLossModel.TryResolveStopPrice(entryTriggerCandidate, instrumentInfo.TickSize);
                    decimal? riskPerTrade = RiskInitialCapital > 0m && RiskPolicyMaxRiskPerTradePercent > 0m
                        ? RiskInitialCapital * (RiskPolicyMaxRiskPerTradePercent / 100m)
                        : null;
                    riskParameters = new TradeRiskParameters(stopLoss, riskPerTrade);
                }

                _latestTradePlan = _tradePlanEngine.Process(new TradePlanContext(entryTriggerCandidate, instrumentInfo, riskParameters));

                if (trace is not null)
                {
                    tradePlanTrace.Complete(
                        PipelineTraceDetails.Create(
                            ("Status", _latestTradePlan.Status),
                            ("Direction", _latestTradePlan.Direction),
                            ("EntryPrice", _latestTradePlan.EntryPrice),
                            ("StopLoss", _latestTradePlan.StopLoss),
                            ("TakeProfit", _latestTradePlan.TakeProfit),
                            ("RiskPerUnit", _latestTradePlan.RiskPerUnit),
                            ("RiskAmount", _latestTradePlan.RiskAmount),
                            ("PositionSize", _latestTradePlan.PositionSize),
                            ("RiskRewardRatio", _latestTradePlan.RiskRewardRatio),
                            ("InvalidationReason", _latestTradePlan.InvalidationReason)),
                        _latestTradePlan.Diagnostics.Count);
                }
            }
            else
            {
                _latestTradePlan = null;
                if (trace is not null)
                {
                    tradePlanTrace.Complete(PipelineTraceDetails.Create(("Status", "N/A - no EntryTriggerCandidate")));
                }
            }
        }
        catch (Exception exception)
        {
            tradePlanTrace.Fail(exception);
            throw;
        }

        RunRiskStage(context, trace, traceCollector);

        // Sprint 15.24 (Lot 1 - chart display): always recomputed from _latestTradePlan, the same
        // object TradingDashboard reads (see IQIAIndicator.cs's DashboardContext.TradePlan assignment
        // below) - guarantees the chart and the HUD panel can never show two different Entry/SL/TP
        // values for the same bar.
        _latestTradePlanAnnotationCandidate = _tradePlanAnnotationEngine.Process(new TradePlanAnnotationContext(_latestTradePlan));

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

        CaptureScientificDataset(bar, context, trace);
    }

    /// <summary>
    /// MarketContext pipeline stage (verbatim extraction from <see cref="OnCalculate"/>): builds this
    /// bar's <see cref="Core.MarketContext"/> via the ATAS builder and validates it. Returns
    /// <c>null</c> when validation fails - the caller must then return early, exactly as the former
    /// inline <c>return</c> did, after the same three trace side effects
    /// (<c>_latestPipelineTrace</c>/<c>_latestPipelineTraceCollector</c>/<c>_latestPipelineTraceReport</c>).
    /// </summary>
    private Core.MarketContext? BuildValidatedMarketContext(
        int bar, PipelineTraceRun? trace, IPipelineTraceCollector? traceCollector)
    {
        PipelineTraceScope marketContextTrace = trace is null
            ? default
            : traceCollector!.BeginStage(trace, PipelineTraceStage.MarketContext);
        try
        {
            Core.MarketContext context = _builder.Build(bar, CurrentBar);
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
                return null;
            }

            return context;
        }
        catch (Exception exception)
        {
            marketContextTrace.Fail(exception);
            throw;
        }
    }

    /// <summary>
    /// RiskEngine pipeline stage (verbatim extraction from <see cref="OnCalculate"/>), strictly after
    /// TradePlan - evaluates the TradePlan this bar already produced, never reconstructs or recomputes
    /// it (Lot 11, Section 7). Purely observational: RiskAssessment is never fed back into
    /// TradePlan/EntryTrigger/Decision and never triggers an order (this indicator sends none,
    /// regardless of Status). AccountState/RiskPolicy/InstrumentRiskSpecification are built fresh from
    /// the Risk Engine parameters every bar - pure, no state carried across bars other than the UI
    /// parameter values themselves. Lot 12: AccountState/InstrumentRiskSpecification are built
    /// unconditionally (out of the "TradePlan exists" branch) purely so the Risk Engine dashboard
    /// panel can show Capital/Equity/Instrument on every bar - only the request/Evaluate call stays
    /// gated on <c>_latestTradePlan</c>. Lot 12.2: CurrentEquity and InstrumentRiskSpecification are
    /// sourced from ATAS itself (Indicator.TradingManager/.TradingStatisticsProvider), read fresh
    /// every bar - no caching. RiskCurrentEquity (Lot 11) is intentionally no longer read here:
    /// Section 5 forbids ever falling back to a manual/stale equity reading - unavailable equity
    /// resolves to the same 0m "unconfigured" sentinel RiskEngine treats as INVALID_EQUITY.
    /// All outputs are instance fields; nothing is returned.
    /// </summary>
    private void RunRiskStage(
        Core.MarketContext context, PipelineTraceRun? trace, IPipelineTraceCollector? traceCollector)
    {
        PipelineTraceScope riskTrace = trace is null ? default : traceCollector!.BeginStage(trace, PipelineTraceStage.Risk);
        try
        {
            // Sprint 15.25 (Lot 12.6, wired into the decision Lot 12.11): TradingManager?.Portfolio
            // .IsReplay() (ATAS.DataFeedsCore.Extensions - confirmed by reflection against the installed
            // ATAS assemblies to check Portfolio.AccountID == "Replay" exactly). A real, non-"Replay"
            // account observed live (Lot 12.10 capture: Portfolio.IsReplay()==false, real AccountID,
            // non-zero Balance distinct from InitialCapital) proved this signal does get populated
            // correctly outside the Replay pseudo-account, which is what makes it safe to now use as
            // evidence rather than observability only - see ATASEquityReplayDetector.cs's doc comment for
            // the full priority rationale (still subordinate to the wall-clock guard immediately below).
            _latestPortfolioIsReplay = TradingManager?.Portfolio?.IsReplay();

            // Sprint 15.25 (Lot 12.6, extended Lot 12.11): replaces context.Execution.IsReplay
            // (Core.MarketContextBuilder's in-house bar-index heuristic - a real Replay capture proved it
            // misclassifies genuinely-replayed bars as "Realtime", see ATASEquityReplayDetector.cs's doc
            // comment) with a corrected signal, used ONLY here to pick Equity's source.
            // ATASAccountStateAdapter.TryGetCurrentEquity itself (Lot 12.2, protected) is completely
            // unchanged - only the isReplay argument it receives is now more accurate; context.Execution
            // .IsReplay itself is left untouched everywhere else in the pipeline (out of this lot's
            // scope). Lot 12.11: _latestPortfolioIsReplay is now passed in (previously computed but never
            // read here) - it takes priority over heuristicIsReplay once ATAS confirms a live, recent,
            // non-Replay account (Lot 12.10's finding); the wall-clock gap guard still cannot be
            // overridden by it in either direction (ATASEquityReplayDetector.cs).
            _latestEquitySourceIsReplay = ATASEquityReplayDetector.IsReplayContext(
                heuristicIsReplay: context.Execution.IsReplay,
                barTime: context.Clock.CurrentTime,
                utcNow: DateTime.UtcNow,
                portfolioIsReplay: _latestPortfolioIsReplay);

            // Sprint 15.25 (Lot 12.12, Problem A/C): wraps the SAME decision just computed above - never
            // a second one - into the explicit AtasDataContext every dashboard consumer now reads
            // (DashboardManager/DebugDashboard/DatasetDashboard - see AtasDataContextResolver.cs).
            _latestAtasDataContext = AtasDataContextResolver.Resolve(hasBeenResolved: true, isReplay: _latestEquitySourceIsReplay);

            decimal? atasEquity = ATASAccountStateAdapter.TryGetCurrentEquity(
                TradingStatisticsProvider, _latestEquitySourceIsReplay);

            // Sprint 15.25 (Lot 12.5): TEMPORARY OBSERVABILITY ONLY - a second, independent read of the
            // same ITradingStatistics.Equity series TryGetCurrentEquity just consulted above (same
            // Realtime/Replay selection, now via _latestEquitySourceIsReplay - Lot 12.6), purely to expose
            // the series' size and the timestamp of its last point for diagnosis (Lot 12.5 brief,
            // Section 2). Never writes to atasEquity/_latestRiskAccountState above and is never read by
            // them - a separate, passive observation, not a second source of truth for CurrentEquity.
            ITradingStatistics? statisticsForTelemetry = TradingStatisticsProvider is null
                ? null
                : _latestEquitySourceIsReplay ? TradingStatisticsProvider.Replay : TradingStatisticsProvider.Realtime;
            _latestAtasEquitySeriesCount = statisticsForTelemetry?.Equity.Count();
            _latestAtasEquityLastTimestamp = statisticsForTelemetry is not null && statisticsForTelemetry.Equity.Any()
                ? statisticsForTelemetry.Equity.Last().Time
                : null;

            _latestRiskAccountState = ATASAccountStateAdapter.Build(
                TradingManager?.Portfolio,
                atasEquity,
                initialCapital: RiskInitialCapital,
                peakEquity: RiskPeakEquity,
                dailyStartingEquity: RiskDailyStartingEquity,
                dailyPnL: RiskDailyPnL,
                riskUsedToday: RiskUsedToday,
                openRisk: RiskOpenRisk);

            RiskPolicy riskPolicy = RiskPolicyFactory.FromRawInputs(
                RiskPolicyMaxRiskPerTradePercent,
                RiskPolicyMaxRiskPerTradeAmount,
                RiskPolicyMaxDailyLossPercent,
                RiskPolicyMaxDailyLossAmount,
                RiskPolicyMaxDrawdownPercent,
                RiskPolicyMaxDrawdownAmount,
                RiskPolicyMaxOpenRiskPercent,
                RiskPolicyMaxOpenRiskAmount,
                RiskPolicyMinRiskReward,
                RiskPolicyMaxPositionSize);

            // Sprint 15.25 (Lot 12.6): QuantityStep stays exclusively the manual parameter, UNCHANGED
            // from before this lot. ATASInstrumentQuantityStepResolver.cs (new this lot) investigated
            // Security.LotSize as a candidate ATAS source, built and unit-tested it - but that same test
            // suite caught, empirically, that a freshly-constructed/never-populated Security already
            // reports LotSize = 1 by SDK default (confirmed by reflection - see the Lot 12.6 report),
            // indistinguishable from a connector genuinely reporting "1" for a real instrument. Wiring it
            // in would have silently reproduced exactly what the brief forbids ("QuantityStep = 1 ...
            // simplement pour faire passer IsValid à true"), without ever writing that literal - the STOP
            // RULE applies (API not sufficiently certain). The resolver/tests are kept, unused, as
            // evidence for a future lot. MinQuantity/MaxQuantity are unaffected: LotMinSize/LotMaxSize
            // are nullable and default to null (not a same-as-real-data sentinel), so
            // ATASInstrumentAdapter.Build's existing fallback for them (Lot 12.2, protected, unchanged)
            // does not share this ambiguity - the two *Source labels below only observe, read-only,
            // which branch that unchanged logic took.
            _latestMinQuantitySource = TradingManager?.Security?.LotMinSize is decimal lotMinForTelemetry && lotMinForTelemetry > 0m
                ? "ATAS.LotMinSize" : ATASInstrumentQuantityStepResolver.ManualSource;
            _latestMaxQuantitySource = TradingManager?.Security?.LotMaxSize is decimal lotMaxForTelemetry && lotMaxForTelemetry > 0m
                ? "ATAS.LotMaxSize" : ATASInstrumentQuantityStepResolver.ManualSource;

            _latestRiskInstrumentSpec = ATASInstrumentAdapter.Build(
                TradingManager?.Security,
                fallbackMinQuantity: RiskInstrumentMinQuantity,
                fallbackMaxQuantity: RiskInstrumentMaxQuantity,
                quantityStep: RiskInstrumentQuantityStep);

            RiskEngineRequest? riskRequest = _latestTradePlan is null
                ? null
                : RiskEngineRequestFactory.FromTradePlan(
                    _latestTradePlan, _latestRiskAccountState, riskPolicy, _latestRiskInstrumentSpec);

            // Sprint 15.25 (Lot 12.5): passive retention only - the exact same object about to be passed
            // to _riskEngine.Evaluate below (or null), kept for ScientificDatasetRecord.From to capture
            // later this bar. Storing a reference does not influence riskRequest or the Evaluate call.
            _latestRiskEngineRequest = riskRequest;

            _latestRiskAssessment = riskRequest is null ? null : _riskEngine.Evaluate(riskRequest);

            // Sprint 15.25 (Lot 12.3): TEMPORARY DIAGNOSTIC capture - read-only, never influences
            // riskRequest/_latestRiskAssessment above (already computed by this point). See
            // ATASRuntimeDiagnostics.cs's doc comment; candidate for removal once the Lot 12.3 root
            // cause is confirmed/addressed.
            _latestATASAccountDiagnostic = ATASRuntimeDiagnostics.CaptureAccount(
                TradingManager?.Portfolio, TradingManager?.Position, TradingStatisticsProvider, _latestRiskAccountState.CurrentEquity);
            _latestATASInstrumentDiagnostic = ATASRuntimeDiagnostics.CaptureInstrument(
                TradingManager?.Security, _latestRiskInstrumentSpec);

            if (trace is not null)
            {
                static string Unavailable(object? value) => value is null ? "UNAVAILABLE" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "UNAVAILABLE";

                riskTrace.Complete(
                    PipelineTraceDetails.Create(
                        ("Status", _latestRiskAssessment?.Status.ToString() ?? "N/A - no assessable TradePlan"),
                        ("RejectionReasons", _latestRiskAssessment is null ? string.Empty : string.Join(", ", _latestRiskAssessment.RejectionReasons)),
                        ("PositionSize", _latestRiskAssessment?.PositionSize),
                        ("RiskAmount", _latestRiskAssessment?.RiskAmount),
                        ("RiskBudget", _latestRiskAssessment?.RiskBudget),
                        ("RiskRewardRatio", _latestRiskAssessment?.RiskRewardRatio),
                        ("ATAS.AccountID", Unavailable(_latestATASAccountDiagnostic.AccountID)),
                        ("ATAS.IsRealAccount", Unavailable(_latestATASAccountDiagnostic.IsRealAccount)),
                        ("ATAS.Currency", Unavailable(_latestATASAccountDiagnostic.Currency)),
                        ("ATAS.Balance", Unavailable(_latestATASAccountDiagnostic.Balance)),
                        ("ATAS.BalanceAvailable", Unavailable(_latestATASAccountDiagnostic.BalanceAvailable)),
                        ("ATAS.BalancePower", Unavailable(_latestATASAccountDiagnostic.BalancePower)),
                        ("ATAS.OpenPnL", Unavailable(_latestATASAccountDiagnostic.OpenPnL)),
                        ("ATAS.ClosedPnL", Unavailable(_latestATASAccountDiagnostic.ClosedPnL)),
                        ("ATAS.TotalPnL", Unavailable(_latestATASAccountDiagnostic.TotalPnL)),
                        ("ATAS.PositionVolume", Unavailable(_latestATASAccountDiagnostic.PositionVolume)),
                        ("ATAS.PositionUnrealizedPnL", Unavailable(_latestATASAccountDiagnostic.PositionUnrealizedPnL)),
                        ("ATAS.PositionRealizedPnL", Unavailable(_latestATASAccountDiagnostic.PositionRealizedPnL)),
                        ("ATAS.RealtimeEquity", Unavailable(_latestATASAccountDiagnostic.RealtimeEquity)),
                        ("ATAS.ReplayEquity", Unavailable(_latestATASAccountDiagnostic.ReplayEquity)),
                        ("ATAS.FinalEquityUsed", _latestATASAccountDiagnostic.FinalEquityUsed),
                        ("ATAS.Instrument", Unavailable(_latestATASInstrumentDiagnostic.Instrument)),
                        ("ATAS.TickSize", Unavailable(_latestATASInstrumentDiagnostic.TickSize)),
                        ("ATAS.TickCost", Unavailable(_latestATASInstrumentDiagnostic.TickCost)),
                        ("ATAS.LotSize", Unavailable(_latestATASInstrumentDiagnostic.LotSize)),
                        ("ATAS.LotMinSize", Unavailable(_latestATASInstrumentDiagnostic.LotMinSize)),
                        ("ATAS.LotMaxSize", Unavailable(_latestATASInstrumentDiagnostic.LotMaxSize)),
                        ("ATAS.Digits", Unavailable(_latestATASInstrumentDiagnostic.Digits)),
                        ("ATAS.BaseCurrency", Unavailable(_latestATASInstrumentDiagnostic.BaseCurrency)),
                        ("ATAS.QuoteCurrency", Unavailable(_latestATASInstrumentDiagnostic.QuoteCurrency)),
                        // Sprint 15.25 (Lot 12.4): the FINAL resolved specification fields (after
                        // ATAS-vs-manual-fallback arbitration, ATASInstrumentAdapter.Build) - distinct
                        // from the raw ATAS.* values above, needed to tell apart "ATAS provided
                        // LotMinSize/LotMaxSize" from "the manual fallback parameter was used instead".
                        ("ATAS.FinalMinQuantity", _latestRiskInstrumentSpec.MinQuantity),
                        ("ATAS.FinalMaxQuantity", _latestRiskInstrumentSpec.MaxQuantity),
                        ("ATAS.FinalQuantityStep", _latestRiskInstrumentSpec.QuantityStep),
                        ("ATAS.SpecificationValid", _latestRiskInstrumentSpec.IsValid),
                        ("ATAS.InvalidFieldReasons", string.Join("; ", _latestATASInstrumentDiagnostic.InvalidFieldReasons))),
                    _latestRiskAssessment?.Diagnostics.Count ?? 0);
            }

            _latestRiskStageError = null;
        }
        catch (Exception exception)
        {
            // Sprint 15.25 (Lot 12.12, Problem B). Every read in the try block above is either pure
            // (RiskPolicyFactory/RiskEngineRequestFactory/RiskEngine.Evaluate - already exercised by
            // Tests/Risk/*, no external dependency) or touches an ATAS-owned property this indicator
            // does not control the initialization timing of (TradingManager/.Portfolio/.Security/
            // TradingStatisticsProvider). Before this lot, ANY exception here (Fail+throw) propagated
            // out of OnCalculate uncaught - crashing this bar's entire REMAINING pipeline (BarIndex,
            // dataset collection, the Risk panel itself) with zero diagnostic trace, and - because ATAS
            // keeps calling OnCalculate again for the next bar against the same unresolved ATAS state -
            // potentially every bar for the rest of the session (see Lot 12.12 report, Problem B: a
            // live capture showing Status/Instrument/Capital/Equity all "NOT AVAILABLE" and BarIndex
            // stuck at 0 despite an active, connected chart). Degrading instead of crashing mirrors the
            // EXACT discipline already used for the ScientificDataset block further down this method
            // ("never let a collection failure disrupt the trading pipeline"): the Risk stage's own
            // outputs fall back to their honest "unavailable" shape (identical to a bar with no ATAS
            // data at all - never a fabricated value), the exact exception is captured and surfaced
            // (never silently swallowed - see _latestRiskStageError, read by the DEBUG dashboard, the
            // RISK ENGINE panel's Reason field, and ATAS.RiskStage.Exception in
            // ScientificDatasetRecord), and - critically - the REST of OnCalculate still runs, so a
            // transient ATAS binding issue can never permanently stall the session. RiskEngine/
            // RiskPolicy/InstrumentRiskSpecification/RiskEngineRequest (all protected) are themselves
            // completely untouched: this only changes how their INPUTS behave when ATAS itself cannot
            // supply them this bar.
            //
            // Audit 2026-08-29: include the InnerException chain. A real ATAS deployment surfaced a
            // TypeInitializationException here whose own Message is only the generic wrapper ("The type
            // initializer for '...' threw an exception.") - the actual cause lives in InnerException and
            // was lost, leaving ATAS.RiskStage.Exception undiagnosable. Walk the chain (also unwraps the
            // AggregateException case) so the captured string carries the root cause.
            _latestRiskStageError = DescribeExceptionChain(exception);
            _latestRiskAccountState = null;
            _latestRiskInstrumentSpec = null;
            _latestRiskEngineRequest = null;
            _latestRiskAssessment = null;
            _latestATASAccountDiagnostic = null;
            _latestATASInstrumentDiagnostic = null;
            riskTrace.Fail(exception);
        }
    }

    /// <summary>
    /// Flattens an exception and its <see cref="Exception.InnerException"/> chain into a single
    /// "Type: message -&gt; InnerType: inner message" string for capture in <c>_latestRiskStageError</c>
    /// (Audit 2026-08-29). <see cref="AggregateException"/> is unwrapped via its first inner exception,
    /// which is what its own <see cref="Exception.InnerException"/> already exposes. The depth guard is a
    /// belt-and-braces stop against a pathological self-referential chain.
    /// </summary>
    private static string DescribeExceptionChain(Exception exception)
    {
        var builder = new System.Text.StringBuilder();
        Exception? current = exception;
        for (int depth = 0; current is not null && depth < 8; depth++)
        {
            if (depth > 0)
                builder.Append(" -> ");
            builder.Append(current.GetType().Name).Append(": ").Append(current.Message);
            current = current.InnerException;
        }

        return builder.ToString();
    }

    /// <summary>
    /// ScientificDataset capture (verbatim extraction from <see cref="OnCalculate"/>). Sprint 15.17
    /// (QDE-012 real-market capture): pure OBSERVER, runs after every trading engine has already
    /// produced its result for this bar. Reads context/pipeline outputs; never writes back into
    /// anything the pipeline reads. Wrapped in try/catch so a failure in this diagnostic/data-collection
    /// path can never propagate into <see cref="OnCalculate"/> and disrupt trading - the same guarantee
    /// EnableScientificDataset=false already gives by construction, extended to unexpected exceptions
    /// while it is on.
    /// </summary>
    private void CaptureScientificDataset(int bar, Core.MarketContext context, PipelineTraceRun? trace)
    {
        if (!EnableScientificDataset)
            return;

        try
        {
            _datasetLifecycleLog.MarkAdd(DateTime.UtcNow);
            // Sprint 15.25 (audit refactor): _latestScientificMarketContext/_latestDecisionResult were
            // proven non-null by OnCalculate's own flow analysis when this block was inline; extracted
            // into a method, the compiler can no longer see those assignments, so the null-forgiveness
            // is now explicit (matching _latestScientificAssessment! which already needed it). Reaching
            // this line still requires OnCalculate to have run every prior stage this bar - behaviour is
            // unchanged.
            _scientificDatasetCollector!.Add(ScientificDatasetRecord.From(
                _scientificDatasetSessionId,
                bar,
                _latestScientificMarketContext!,
                _latestScientificAssessment!,
                _latestDecisionResult!,
                open: context.Price.Open,
                high: context.Price.High,
                low: context.Price.Low,
                volume: context.Volume.Volume,
                trace: trace,
                // Sprint 15.25 (Lot 9): passive instrumentation only - these are the same
                // already-computed objects the dashboard/renderer below already read this bar
                // (_latestEntryCandidate, _signalEngine.LastEntryTriggerCandidate, _latestTradePlan);
                // nothing here is recomputed or influenced by being captured.
                entryCandidate: _latestEntryCandidate,
                entryTriggerCandidate: _signalEngine.LastEntryTriggerCandidate,
                tradePlan: _latestTradePlan,
                riskAssessment: _latestRiskAssessment,
                // Sprint 15.25 (Lot 12.5): same passive-capture discipline as above - these are the
                // same already-computed objects the Risk stage produced this bar
                // (_latestATASAccountDiagnostic/_latestATASInstrumentDiagnostic since Lot 12.3;
                // _latestRiskAccountState/_latestRiskInstrumentSpec since Lot 12.2;
                // _latestRiskEngineRequest/_latestAtasEquitySeriesCount/_latestAtasEquityLastTimestamp
                // new to this lot but populated the same way, right where the Risk stage already
                // computes their sources) - nothing here is recomputed.
                atasAccountDiagnostic: _latestATASAccountDiagnostic,
                atasInstrumentDiagnostic: _latestATASInstrumentDiagnostic,
                atasEquityIsReplay: _latestEquitySourceIsReplay,
                atasEquitySeriesCount: _latestAtasEquitySeriesCount,
                atasEquityLastTimestamp: _latestAtasEquityLastTimestamp,
                riskAccountState: _latestRiskAccountState,
                riskInstrumentSpec: _latestRiskInstrumentSpec,
                riskEngineRequest: _latestRiskEngineRequest,
                // Sprint 15.25 (Lot 12.6): same passive-capture discipline - these are the same
                // already-computed values the Risk stage produced this bar
                // (_latestPortfolioIsReplay/_latestMinQuantitySource/_latestMaxQuantitySource,
                // ATASEquityReplayDetector.cs) - nothing here is recomputed.
                atasEquityHeuristicIsReplay: context.Execution.IsReplay,
                atasPortfolioIsReplay: _latestPortfolioIsReplay,
                minQuantitySource: _latestMinQuantitySource,
                maxQuantitySource: _latestMaxQuantitySource,
                // Sprint 15.25 (Lot 12.12): same passive-capture discipline - _latestAtasDataContext/
                // _latestRiskStageError are the same already-computed values the Risk stage produced
                // this bar (AtasDataContextResolver.cs / the Risk stage's catch clause) - nothing
                // here is recomputed.
                atasContext: _latestAtasDataContext,
                riskStageError: _latestRiskStageError));

            // Sprint 15.17.2: throttled so the persistent lifecycle snapshot stays reasonably
            // current without adding per-bar disk I/O to the ATAS calculation hot path.
            if (_scientificDatasetCollector.TotalAddAttempts % LifecycleSnapshotWriteEveryNBars == 0)
                WriteLifecycleSnapshot();
        }
        catch (Exception)
        {
            // Diagnostic-only path: never let a collection failure disrupt the trading pipeline
            // above. ScientificDatasetCollector.RejectedReason/InvalidRecordsRejected already
            // surface expected rejections (invalid OHLCV, duplicates) without throwing - reaching
            // this catch means something unexpected happened; it is intentionally swallowed here
            // rather than crashing the indicator, and is visible only via the collector's own
            // counters not increasing for this bar.
        }
    }

    /// <summary>
    /// Sprint 15.17 (QDE-012 real-market capture): ATAS's own BaseIndicator exposes a real,
    /// protected virtual OnDispose() hook (confirmed by reflection against the installed
    /// ATAS.Indicators.dll - Indicator implements IDisposable via BaseIndicator/ExtendedIndicator,
    /// both of which declare "public virtual void Dispose()" calling into this). This is the one
    /// reliable "indicator is being removed from the chart" signal available anywhere in the ATAS
    /// SDK surface this project references - there is no OnStop/OnClose/OnReplayFinished callback.
    /// Exporting here closes the gap the Sprint 15.17 audit found: without it, EnableScientificDataset
    /// accumulates records in memory with no automatic flush, and ExportScientificDataset() has no
    /// caller anywhere in the live UI.
    ///
    /// Sprint 15.17.1: whether ATAS actually calls this on indicator removal (vs. only on
    /// application shutdown, or some other lifecycle event) is still NOT proven by this override
    /// existing - see DatasetLifecycleLog.OnDisposeEnteredAt, set unconditionally as the very first
    /// statement below, specifically so a real ATAS session can answer that question by checking
    /// whether this timestamp got set at all. ExportScientificDataset() itself no longer swallows
    /// failures (Core/Calibration/ScientificDatasetExporter.cs catches and records them into
    /// ExportResult) - the try/catch here is a last-resort safety net for anything failing OUTSIDE
    /// that boundary, so base.OnDispose() is still guaranteed to run no matter what.
    /// </summary>
    protected override void OnDispose()
    {
        _datasetLifecycleLog.MarkOnDisposeEntered(DateTime.UtcNow);
        // Sprint 15.19 (QDE-012 forming-bar-capture fix): the collector may still be holding a
        // still-forming bar that no later callback has ever proven closed (ATAS gives no advance
        // warning that the indicator is about to be removed). Flush() applies the documented
        // EXCLUDE_CURRENT_FORMING_BAR policy - that bar is never committed to the exported dataset -
        // and must run BEFORE ExportScientificDataset() below so the export it produces reflects the
        // decision. See ScientificDatasetCollector.Flush()'s doc comment and the QDE-012_Sprint_15.19
        // report §7 for why exporting a still-forming bar as if it were closed is exactly the defect
        // this sprint fixes.
        _scientificDatasetCollector?.Flush();
        // Sprint 15.17.2: persisted BEFORE any export attempt below, exactly per the brief's Phase 4
        // ordering requirement - this is what lets a real ATAS session prove OnDispose fired even if
        // the export that follows crashes in some way this sprint's try/catch didn't anticipate.
        WriteLifecycleSnapshot();
        try
        {
            if (EnableScientificDataset && !_scientificDatasetAutoExportAttempted)
            {
                _scientificDatasetAutoExportAttempted = true;
                ExportScientificDataset();
            }

            // Sprint 15.23.1: write the capped Kalman window diagnostic (see
            // EnableKalmanWindowDiagnostic) next to the scientific dataset export, once, at session
            // end - never mid-session, matching ExportScientificDataset()'s own timing.
            if (EnableKalmanWindowDiagnostic && KalmanFilterModel.Diagnostics.Count > 0)
            {
                Directory.CreateDirectory(ScientificDatasetOutputDirectory);
                string fileName = $"KalmanWindowDiagnostic_{_scientificDatasetSessionId:N}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
                string path = Path.Combine(ScientificDatasetOutputDirectory, fileName);
                File.WriteAllText(path, JsonSerializer.Serialize(KalmanFilterModel.Diagnostics, new JsonSerializerOptions { WriteIndented = true }));
                LastKalmanWindowDiagnosticPath = path;
            }
        }
        catch (Exception)
        {
            // Last-resort safety net only: ExportScientificDataset()/ScientificDatasetExporter.Export
            // already catch and record every export failure into _lastExportResult
            // (ExportOutcome.Failed) without throwing. Reaching this catch means something failed
            // outside that boundary - still must never block base.OnDispose() below.
        }
        finally
        {
            base.OnDispose();
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

            // Sprint 15.24 (Lot 1 - chart display): draws Entry/TakeProfit/StopLoss directly on the
            // price chart, using ATAS's real price-to-pixel conversion (IChartContainer.GetYByPrice) -
            // reads only the same _latestTradePlanAnnotationCandidate the dashboard section below is
            // built from a few lines down (both ultimately trace back to _latestTradePlan). Guarded the
            // same way _atasRenderer.Render(...) above is: skipped if ATAS hasn't attached a chart yet.
            if (_latestTradePlanAnnotationCandidate is not null && ChartInfo?.PriceChartContainer is IChartContainer priceContainer)
            {
                _atasRenderer.RenderTradePlan(renderContext, priceContainer, ChartArea, _latestTradePlanAnnotationCandidate);
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
                TradePlan = _latestTradePlan,
                RiskAssessment = _latestRiskAssessment,
                RiskAccount = _latestRiskAccountState,
                RiskInstrument = _latestRiskInstrumentSpec,
                AtasContext = _latestAtasDataContext,
                RiskStageError = _latestRiskStageError,
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
                DatasetStartTime = _scientificDatasetCollector is null ? null : _scientificDatasetStartTime,
                LastExportResult = _lastExportResult,
                DatasetLifecycleLog = _datasetLifecycleLog,
                LifecycleSnapshotPath = _lastLifecycleSnapshotPath,
                LifecycleSnapshotError = _lastLifecycleSnapshotError
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
