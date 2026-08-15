using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        new RandomWalkRule()
    ]);
    private readonly FusionStateManager     _fusionState  = new();
    private readonly MethodologyEngine       _methodologyEngine = new();
    private readonly SignalEngine            _signalEngine = new();
    private readonly TradePlanEngine         _tradePlanEngine = new();
    private readonly TradePlanAnnotationEngine _tradePlanAnnotationEngine = new();
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
            _builder = CreateBuilder();

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

                // Sprint 15.8 (Phase 1 audit): no Risk Engine / stop-loss methodology / account risk
                // budget exists anywhere in the system yet, so RiskParameters is left null - the
                // TradePlan will honestly report SIGNAL_ONLY rather than fabricate SL/sizing.
                _latestTradePlan = _tradePlanEngine.Process(new TradePlanContext(entryTriggerCandidate, instrumentInfo));

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

        // Sprint 15.17 (QDE-012 real-market capture): pure OBSERVER, placed after every trading engine
        // above has already produced its result for this bar. Reads context/pipeline outputs; never
        // writes back into anything the pipeline reads. Wrapped in try/catch so that a failure in this
        // diagnostic/data-collection path can never propagate into OnCalculate and disrupt trading -
        // the same guarantee EnableScientificDataset=false already gives by construction (this block
        // doesn't run at all), extended to cover unexpected exceptions while it's on.
        if (EnableScientificDataset)
        {
            try
            {
                _datasetLifecycleLog.MarkAdd(DateTime.UtcNow);
                _scientificDatasetCollector!.Add(ScientificDatasetRecord.From(
                    _scientificDatasetSessionId,
                    bar,
                    _latestScientificMarketContext,
                    _latestScientificAssessment!,
                    _latestDecisionResult,
                    open: context.Price.Open,
                    high: context.Price.High,
                    low: context.Price.Low,
                    volume: context.Volume.Volume,
                    trace: trace));

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
