using System;
using System.Collections.Generic;
using System.Text;
using IQIAIndicator.Backtest.Cost;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Measurement;
using IQIAIndicator.Backtest.Pnl;
using IQIAIndicator.Backtest.Risk;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Entry;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Fusion.State;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.Presentation;
using IQIAIndicator.Engine.Regime;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Engine.ScientificFusion;
using IQIAIndicator.Engine.Signal;
using IQIAIndicator.Engine.TradePlan;
using DecisionRules = IQIAIndicator.Engine.Decision.Rules;
using FusionEngine = IQIAIndicator.Engine.Fusion.EvidenceFusionEngine;
using ScientificMarketContext = IQIAIndicator.Engine.ScientificModels.Abstractions.MarketContext;

namespace IQIAIndicator.Backtest;

/// <summary>
/// Sprint 15.25 (Lot 14.1, brief §11). Foundation-level orchestrator: walks a <see cref="BacktestScenario"/>'s
/// historical bars in order, builds a <see cref="MarketContext"/> for each through the SAME
/// <see cref="MarketContextFactory"/> the live ATAS path now also uses (Lot 13 report §6.3, applied
/// §8 of this lot), validates it, and runs the real, unmodified <see cref="RegimeEngine"/> on it.
///
/// THIS LOT DOES NOT GO FURTHER. Fusion, Decision, MethodologySelection, SignalEngine, Entry,
/// EntryTrigger, TradePlan, RiskEngine and any execution/account/PnL machinery are explicitly out of
/// scope (brief §1/§22/§23) - later lots (14.3 onward, see the companion report §28) plug into this same
/// loop, at the point marked below, without altering what already runs here.
///
/// STATELESS BY DESIGN: this class carries no field. Every call to <see cref="Run"/> constructs its own
/// <see cref="RegimeEngine"/> and <see cref="MarketContextValidator"/> instances, so two runs (e.g. one
/// per <see cref="BacktestWindow"/>) can never share state - the exact isolation rule the Lot 13 report
/// flagged as missing from <c>FusionStateManager</c> (no reset hook exists there), extended here as a
/// standing rule for every future stateful component this engine will eventually own (brief §12).
/// </summary>
public sealed class BacktestEngine
{
    /// <param name="scenario">Fully validated scenario (see <see cref="BacktestScenario.TryCreate"/>).</param>
    /// <param name="warmupBars">How many leading bars (by index, 0-based) are still considered warmup.
    /// Deliberately NOT defaulted - the Lot 13 report found the pipeline's real evidence models need
    /// between 20 and 128 bars depending on which evidence, and this lot must not invent a new
    /// "one true" constant (brief §13). The caller (a scenario or a test) states it explicitly.</param>
    /// <param name="observer">Optional, passive, read-only hook - see <see cref="IBacktestBarObserver"/>.
    /// Never influences the run; exists so tests can inspect per-bar output without duplicating this loop.</param>
    public BacktestFoundationResult Run(BacktestScenario scenario, int warmupBars, IBacktestBarObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        if (warmupBars < 0)
            throw new ArgumentOutOfRangeException(nameof(warmupBars), warmupBars, "WarmupBars cannot be negative.");

        // Fresh, run-local instances only - see the class doc comment on isolation. Future lots adding
        // FusionStateManager/SignalEngine/etc. to this method must follow the exact same "new Xxx() here,
        // never a field" pattern.
        var regimeEngine = new RegimeEngine();
        var validator = new MarketContextValidator();

        // Sprint 15.25 (Lot 14.1): BacktestScenario carries an InstrumentRiskSpecification (Engine.Risk),
        // not a Core.InstrumentInfo - the two overlap on Symbol/TickSize/TickValue/PointValue but
        // InstrumentRiskSpecification has no "Decimals" (display rounding) field, and none was requested
        // of BacktestScenario by this lot's brief (§10). Decimals has no consumer anywhere in this
        // codebase (verified: no ".Decimals" read outside InstrumentInfo's own declaration) - it is pure
        // UI-facing metadata on the live path (IQIAIndicator.PriceDecimals). 0 is therefore a neutral
        // placeholder, not a fabricated value with any downstream effect; it carries no information this
        // lot's InstrumentRiskSpecification does not already state via TickSize.
        var instrumentInfo = new InstrumentInfo(
            scenario.Instrument.Symbol,
            scenario.Instrument.TickSize,
            scenario.Instrument.TickValue,
            scenario.Instrument.PointValue,
            Decimals: 0);

        HistoricalSeries series = scenario.Series;
        DateTime firstBarTimestamp = series.FirstTimestamp;

        int barsProcessed = 0;
        int barsRejected = 0;
        int warmupCount = 0;
        var fingerprint = new StringBuilder();

        for (int index = 0; index < series.Count; index++)
        {
            // Step 4/5 (brief §11): build the Core.MarketContext for THIS bar only and validate it exactly
            // as the live pipeline does. Extracted to BuildValidatedContext (Sprint 15.25, Lot 14.3) so
            // RunSignalPipeline can share the identical, already-proven-look-ahead-safe construction
            // instead of restating it - a pure extraction, not a behavior change: see the Lot 14.3 report
            // for confirmation that every Lot 14.1 test (BacktestFoundationLookAheadTests included) still
            // passes unmodified after this refactor.
            (MarketContext context, ValidationResult validation) =
                BuildValidatedContext(series, index, instrumentInfo, firstBarTimestamp, validator);

            if (!validation.IsValid)
            {
                barsRejected++;
                observer?.OnBarRejected(index, context, validation.Errors);
                // Mirrors IQIAIndicator.OnCalculate's own early return on an invalid context (Lot 13
                // report §2.1, step [2]): an invalid bar never reaches RegimeEngine, so its own state
                // (the 128-bar circular buffer) is not advanced for this bar either.
                continue;
            }

            bool isWarmup = index < warmupBars;
            if (isWarmup)
                warmupCount++;
            barsProcessed++;

            // Step 6: the real, unmodified RegimeEngine - the same class the live ATAS path calls.
            EvidenceSet evidence = regimeEngine.Collect(context);

            BacktestFingerprint.AppendBar(fingerprint, index, isWarmup, context, evidence);
            observer?.OnBarProcessed(index, isWarmup, context, evidence);

            // Step 7 (brief §11): this is the exact point a later lot (14.3+) will branch into Fusion ->
            // Decision -> MethodologySelection -> SignalEngine -> Entry -> EntryTrigger -> TradePlan ->
            // RiskEngine -> execution, using `context` and `evidence` already computed above. Nothing is
            // wired here yet - see the companion report §28 for the planned order.
        }

        string scenarioId = BacktestFingerprint.ComputeScenarioId(scenario);
        string deterministicHash = BacktestFingerprint.Sha256Hex(fingerprint.ToString());

        return new BacktestFoundationResult(
            BarsProcessed: barsProcessed,
            BarsRejected: barsRejected,
            WarmupBars: warmupCount,
            FirstTimestamp: series.FirstTimestamp,
            LastTimestamp: series.LastTimestamp,
            ScenarioId: scenarioId,
            DeterministicHash: deterministicHash);
    }

    /// <summary>
    /// Sprint 15.25 (Lot 14.3, brief §3/§11/§43). Runs the FULL IQIA signal pipeline - MarketContext -&gt;
    /// Regime -&gt; Fusion -&gt; Decision -&gt; Methodology -&gt; Signal -&gt; Entry -&gt; EntryTrigger -&gt; TradePlan - one
    /// bar at a time, in chronological order, over an already-validated <see cref="BacktestScenario"/>.
    ///
    /// Deliberately a SEPARATE method from <see cref="Run"/>, not a replacement or an overload of it:
    /// <see cref="Run"/> is exercised by 87 Lot 14.1 tests against its exact <see cref="BacktestFoundationResult"/>
    /// contract (brief §37 forbids editing old tests to make a new lot pass), and changing its signature or
    /// return type would be exactly the kind of unrequested refactor brief §43 forbids. The only change
    /// made to <see cref="Run"/> itself is the <see cref="BuildValidatedContext"/> extraction below - a
    /// pure move of two existing lines into a shared, reused helper, verified behavior-identical by the
    /// full pre-existing Lot 14.1 suite still passing (Lot 14.3 report §Tests).
    ///
    /// STATELESS ORCHESTRATOR, same rule as <see cref="Run"/>: every engine below - including the four
    /// that carry no mutable field at all (DecisionEngine/EvidenceFusionEngine(Fusion)/MethodologyEngine/
    /// SignalEngine/TradePlanEngine, verified by inspection) - is constructed fresh, local to this one
    /// call, never as a field on <see cref="BacktestEngine"/>. <see cref="FusionStateManager"/> is the one
    /// genuinely stateful component this lot wires in (EMA/hysteresis smoothing across bars); a fresh
    /// instance per call is what makes Run Isolation (brief §23) hold for it, exactly as Lot 14.1 already
    /// established for <see cref="RegimeEngine"/>'s internal buffer.
    ///
    /// RULE SETS: the DecisionEngine/FusionEngine rule lists below are the exact same rule CLASSES, in the
    /// exact same order, that <c>IQIAIndicator.cs</c> constructs for the live ATAS path - never a
    /// re-implementation, never a subset/superset (brief §3: "Le Backtest doit reproduire le comportement
    /// existant, pas créer une seconde version du pipeline").
    ///
    /// RISK ENGINE / EXECUTION: neither is referenced anywhere in this method (brief §31/§32) - the walk
    /// stops at TradePlan.
    ///
    /// EXCEPTION POLICY (brief §18, decision documented here as required): a stage that throws for one bar
    /// is caught, the failing stage/exception type/message is recorded on that bar's
    /// <see cref="BacktestSignalResult"/> (never `catch { return null; }`), and the RUN CONTINUES to the
    /// next bar - it is never escalated to a whole-run failure. This mirrors the live pipeline's own
    /// resilience convention for non-trading-critical stages (see IQIAIndicator.OnCalculate's Risk-stage
    /// and ScientificDataset-collection catch blocks) and, more importantly, is the only sane choice for a
    /// multi-thousand-bar offline replay: one degenerate bar must not silently discard every bar around it.
    /// KNOWN LIMITATION this policy accepts rather than papers over: <see cref="FusionStateManager"/>
    /// (protected, unmodified) mutates its internal <c>_previousStableResult</c> field as a side effect
    /// partway through <see cref="FusionStateManager.Update"/> before returning - if a LATER line inside
    /// that same call were to throw (not observed in this lot's tests, but not structurally impossible),
    /// the state carried into the NEXT bar would already reflect the failed call. This is not introduced by
    /// this lot: the identical risk exists, unaddressed, on the live ATAS path today, and fixing it would
    /// mean modifying a protected file - explicitly out of scope (brief §1/§42).
    /// </summary>
    /// <param name="scenario">Fully validated scenario (see <see cref="BacktestScenario.TryCreate"/>).</param>
    /// <param name="warmupBars">Same convention as <see cref="Run"/> - a Backtest-level label (brief §5),
    /// never a new engine concept. Bars below this threshold still run the full pipeline; only their
    /// <see cref="BacktestSignalResult.Status"/> differs (Warmup vs Ready).</param>
    public BacktestSignalPipelineResult RunSignalPipeline(BacktestScenario scenario, int warmupBars)
        => RunSignalPipeline(scenario, warmupBars, PipelineParameterOverrides.None);

    /// <summary>
    /// Sprint 15.25 (Lot 14.10, P0-3). Identical to <see cref="RunSignalPipeline(BacktestScenario, int)"/>
    /// in every respect except that <paramref name="overrides"/> is threaded into the
    /// <see cref="Engine.Signal.SignalEngine"/> this call constructs (brief's "RÈGLE DE NON-RÉGRESSION": the
    /// two-argument overload above is the only thing every pre-Lot-14.10 caller ever sees, and it delegates
    /// here with <see cref="PipelineParameterOverrides.None"/>, which reproduces its exact prior behaviour -
    /// verified by <c>PipelineParameterBindingDefaultBehaviourTests</c>).
    /// </summary>
    public BacktestSignalPipelineResult RunSignalPipeline(BacktestScenario scenario, int warmupBars, PipelineParameterOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(overrides);
        if (warmupBars < 0)
            throw new ArgumentOutOfRangeException(nameof(warmupBars), warmupBars, "WarmupBars cannot be negative.");

        var regimeEngine = new RegimeEngine();
        var validator = new MarketContextValidator();
        var fusionEngine = new FusionEngine(
        [
            new StationarityRule(),
            new PersistenceRule(),
            new MeanReversionRule(),
            new RandomWalkRule()
        ]);
        var fusionState = new FusionStateManager();
        var decisionEngine = new DecisionEngine(
        [
            new DecisionRules.StableRangeRule(),
            new DecisionRules.TrendingRule(),
            new DecisionRules.MeanRevertingRule(),
            new DecisionRules.StructuralBreakRule(),
            new DecisionRules.RandomWalkRule()
        ]);
        var methodologyEngine = new MethodologyEngine();
        // Sprint 15.25 (Lot 14.10, P0-3): traceCollector stays null (unchanged from every prior lot);
        // ambiguityGateThreshold falls back to the production constant when overrides.AmbiguityGateThreshold
        // is null (PipelineParameterOverrides.None, or any override that leaves this one field unset).
        var signalEngine = new SignalEngine(
            traceCollector: null,
            ambiguityGateThreshold: overrides.AmbiguityGateThreshold ?? EntryTriggerBuilder.AmbiguityGateThreshold);
        var tradePlanEngine = new TradePlanEngine();

        var instrumentInfo = new InstrumentInfo(
            scenario.Instrument.Symbol,
            scenario.Instrument.TickSize,
            scenario.Instrument.TickValue,
            scenario.Instrument.PointValue,
            Decimals: 0);

        HistoricalSeries series = scenario.Series;
        DateTime firstBarTimestamp = series.FirstTimestamp;

        var bars = new List<BacktestSignalResult>(series.Count);
        var fingerprint = new StringBuilder();

        int barsProcessed = 0, barsRejected = 0, warmupCount = 0;
        int regimeDetected = 0, decisionCount = 0, signalCount = 0, entryCandidateCount = 0;
        int buyCount = 0, sellCount = 0, noActionCount = 0, watchCount = 0;
        int tpSignalOnly = 0, tpReady = 0, tpNoTrade = 0, tpBlocked = 0, exceptionCount = 0;

        for (int index = 0; index < series.Count; index++)
        {
            (MarketContext context, ValidationResult validation) =
                BuildValidatedContext(series, index, instrumentInfo, firstBarTimestamp, validator);

            if (!validation.IsValid)
            {
                barsRejected++;
                var rejected = new BacktestSignalResult(
                    index, context.Clock.CurrentTime, BacktestSignalStatus.Rejected,
                    string.Join("; ", validation.Errors),
                    null, null, null, null, null, null, null, null, null);
                bars.Add(rejected);
                continue;
            }

            bool isWarmup = index < warmupBars;
            if (isWarmup)
                warmupCount++;
            barsProcessed++;

            EvidenceSet? evidence = null;
            DecisionResult? decision = null;
            MethodologySelection? methodology = null;
            OpportunityPresentation? signal = null;
            EntryCandidate? entry = null;
            EntryTriggerCandidate? entryTrigger = null;
            TradePlan? tradePlan = null;
            string stage = "Regime";

            try
            {
                // Regime: the real, unmodified RegimeEngine - identical to Run() and to the live path.
                evidence = regimeEngine.Collect(context);

                stage = "Fusion";
                // EvaluationId is a Guid tagging field only (verified by inspection: declared on
                // FusionContext, never read by any IFusionRule/consumer anywhere in Engine/). The live
                // path uses Guid.NewGuid() purely for telemetry correlation; a backtest replaces it with
                // the deterministic Guid.Empty so two runs over the same scenario are bit-identical
                // without needing to special-case this one inert field out of the fingerprint.
                FusionResult fusionResult = fusionEngine.Fuse(new FusionContext
                {
                    Evidence = evidence,
                    Timestamp = evidence.Timestamp,
                    Symbol = context.Instrument.Symbol,
                    TimeFrame = context.TimeFrame,
                    EvaluationId = Guid.Empty
                });
                FusionSnapshot fusionSnapshot = fusionState.Update(fusionResult, evidence.Timestamp);

                stage = "Decision";
                decision = decisionEngine.Evaluate(new DecisionContext
                {
                    FusionResult = fusionSnapshot.StableResult,
                    Evidence = evidence
                });
                if (decision.Winner != MarketState.Unknown)
                    regimeDetected++;
                if (decision.Candidates.Length > 0)
                    decisionCount++;

                stage = "Methodology";
                methodology = methodologyEngine.Evaluate(decision);

                stage = "Signal";
                ScientificMarketContext scientificContext = BuildScientificMarketContext(series, index, context);
                signal = signalEngine.Process(scientificContext, methodology);
                entry = signalEngine.LastEntryCandidate;
                entryTrigger = signalEngine.LastEntryTriggerCandidate;

                if (signalEngine.LastScientificAssessment is { } assessment && assessment.ExecutedModels.Count > 0)
                    signalCount++;
                if (entry is not null && entry.OpportunityStatus != OpportunityStatus.NOT_QUALIFIED)
                    entryCandidateCount++;
                if (entryTrigger is not null)
                {
                    switch (entryTrigger.Assessment.Direction)
                    {
                        case DirectionCandidate.BUY_CANDIDATE: buyCount++; break;
                        case DirectionCandidate.SELL_CANDIDATE: sellCount++; break;
                        case DirectionCandidate.NO_ACTION: noActionCount++; break;
                        case DirectionCandidate.WATCH: watchCount++; break;
                    }
                }

                stage = "TradePlan";
                // Mirrors IQIAIndicator.OnCalculate exactly: TradePlan is only built when SignalEngine
                // produced an EntryTriggerCandidate this bar (brief §13: never fabricate a TradePlan).
                if (entryTrigger is not null)
                {
                    // Sprint 15.25 (Lot 15.3): the one place TradeRiskParameters is ever resolved to a
                    // non-null value for this pipeline - previously always null (Lot 15.0 P0 finding),
                    // structurally blocking TradePlanStatus.PLAN_READY for every regime including
                    // MeanReverting. Computed ONLY for a directional candidate (never for NO_ACTION/WATCH -
                    // brief §20) and NEVER for an unsupported regime (structurally impossible: Lot 15.1
                    // guarantees Direction is BUY/SELL only when Winner==MeanReverting). StopLoss comes
                    // from VolatilityStopLossModel (Engine.Risk, new in this lot) using VolatilityModel's
                    // already-computed, already-causal CurrentVolatility - never a new evidence
                    // computation. RiskPerTrade reuses the SAME scenario.Policy/scenario.InitialCapital the
                    // real RiskEngine.Evaluate path (RunFullBacktestWithRisk) already validates at
                    // construction (BacktestScenario.Create) - never a new, second risk-budget concept; null
                    // when MaxRiskPerTradePercent is not configured, never a fabricated fallback percentage.
                    // This is a lightweight, per-bar ESTIMATE only (no running-equity tracking, unlike
                    // BacktestRiskResultBuilder's sequential walk) - see the Lot 15.3 report §12 for why
                    // that gap is deliberate and unaddressed here.
                    TradeRiskParameters? riskParameters = null;
                    if (entryTrigger.Assessment.Direction is DirectionCandidate.BUY_CANDIDATE or DirectionCandidate.SELL_CANDIDATE)
                    {
                        decimal? stopLoss = VolatilityStopLossModel.TryResolveStopPrice(entryTrigger, instrumentInfo.TickSize);
                        decimal? riskPerTrade = scenario.Policy.MaxRiskPerTradePercent is decimal riskPercent
                            ? scenario.InitialCapital * riskPercent
                            : null;
                        riskParameters = new TradeRiskParameters(stopLoss, riskPerTrade);
                    }

                    tradePlan = tradePlanEngine.Process(new TradePlanContext(entryTrigger, instrumentInfo, riskParameters));
                    switch (tradePlan.Status)
                    {
                        case TradePlanStatus.SIGNAL_ONLY: tpSignalOnly++; break;
                        case TradePlanStatus.PLAN_READY: tpReady++; break;
                        case TradePlanStatus.NO_TRADE: tpNoTrade++; break;
                        case TradePlanStatus.PLAN_BLOCKED: tpBlocked++; break;
                    }
                }

                var ready = new BacktestSignalResult(
                    index, context.Clock.CurrentTime,
                    isWarmup ? BacktestSignalStatus.Warmup : BacktestSignalStatus.Ready,
                    null, context, evidence, decision, methodology, signal, entry, entryTrigger, tradePlan, null);
                bars.Add(ready);
                BacktestSignalFingerprint.AppendSignalBar(fingerprint, ready);
            }
            catch (Exception exception)
            {
                exceptionCount++;
                var failed = new BacktestSignalResult(
                    index, context.Clock.CurrentTime, BacktestSignalStatus.Exception,
                    $"{stage} stage threw {exception.GetType().Name}.",
                    context, evidence, decision, methodology, signal, entry, entryTrigger, tradePlan,
                    new BacktestStageException(stage, exception.GetType().Name, exception.Message));
                bars.Add(failed);
                BacktestSignalFingerprint.AppendSignalBar(fingerprint, failed);
            }
        }

        string scenarioId = BacktestFingerprint.ComputeScenarioId(scenario);
        string deterministicHash = BacktestFingerprint.Sha256Hex(fingerprint.ToString());

        return new BacktestSignalPipelineResult(
            BarsProcessed: barsProcessed,
            BarsRejected: barsRejected,
            WarmupBars: warmupCount,
            ReadyBars: barsProcessed - warmupCount,
            RegimeDetectedCount: regimeDetected,
            DecisionCount: decisionCount,
            SignalCount: signalCount,
            EntryCandidateCount: entryCandidateCount,
            BuyCount: buyCount,
            SellCount: sellCount,
            NoActionCount: noActionCount,
            WatchCount: watchCount,
            TradePlanSignalOnlyCount: tpSignalOnly,
            TradePlanReadyCount: tpReady,
            TradePlanNoTradeCount: tpNoTrade,
            TradePlanBlockedCount: tpBlocked,
            ExceptionCount: exceptionCount,
            FirstTimestamp: series.FirstTimestamp,
            LastTimestamp: series.LastTimestamp,
            ScenarioId: scenarioId,
            DeterministicHash: deterministicHash,
            Bars: bars.AsReadOnly());
    }

    /// <summary>Shared by <see cref="Run"/> and <see cref="RunSignalPipeline"/> - see the doc comment on
    /// the call site in <see cref="Run"/> for why this is a pure extraction, not a behavior change. Never
    /// reads <paramref name="series"/>.Bars beyond <paramref name="index"/> - the entire look-ahead
    /// boundary (brief §4) lives in this one place for both entry points.</summary>
    private static (MarketContext Context, ValidationResult Validation) BuildValidatedContext(
        HistoricalSeries series,
        int index,
        InstrumentInfo instrumentInfo,
        DateTime firstBarTimestamp,
        MarketContextValidator validator)
    {
        HistoricalBar bar = series.Bars[index];
        MarketContext context = MarketContextFactory.CreateHistorical(
            bar, index, series.Count, series.TimeFrame, instrumentInfo, firstBarTimestamp);
        ValidationResult validation = validator.Validate(context);
        return (context, validation);
    }

    /// <summary>
    /// Sprint 15.25 (Lot 14.3). Backtest-side equivalent of <c>IQIAIndicator.CreateScientificMarketContext</c>
    /// (IQIAIndicator.cs) - same 500-close rolling window, same field mapping - sourced from
    /// <see cref="HistoricalSeries.Bars"/> instead of ATAS's <c>GetCandle</c>. <paramref name="index"/> is
    /// the upper bound: <c>startIndex..index</c> inclusive, never <c>index+1</c> or beyond (brief §4).
    /// Deliberately NOT added to <see cref="MarketContextFactory"/> (Core/): that type is ATAS-free by
    /// construction and depends only on Core (see its own doc comment); this method depends on
    /// Engine.ScientificModels.Abstractions, which would introduce a Core -&gt; Engine dependency Core does
    /// not have today. It lives here, in Backtest/, which already depends on Engine.Regime and now on the
    /// rest of the Engine pipeline for this lot - the correct layer for this glue.
    /// </summary>
    private static ScientificMarketContext BuildScientificMarketContext(HistoricalSeries series, int index, MarketContext context)
    {
        const int historyWindow = 500;
        int startIndex = Math.Max(0, index - historyWindow + 1);
        var history = new List<decimal>(Math.Min(historyWindow, index + 1));

        for (int i = startIndex; i <= index; i++)
            history.Add(series.Bars[i].Close);

        return new ScientificMarketContext(
            context.Clock.CurrentTime,
            context.Execution.CurrentBar,
            history.AsReadOnly(),
            context.Price.Close,
            context.Instrument.Symbol,
            context.TimeFrame,
            null,
            context.Clock.Session.Name);
    }

    /// <summary>
    /// Sprint 15.25 (Lot 14.4, brief §39/§40). Minimal integration point: runs the signal pipeline
    /// (Lot 14.3, completely unchanged), then measures every one of its bars (Lot 14.4). The two steps
    /// are strictly sequential and one-directional - <see cref="RunSignalPipeline"/> is called and fully
    /// returns BEFORE <see cref="ScientificMeasurementEngine.MeasureAll"/> ever runs, so no future bar's
    /// measurement can reach back and alter a signal already produced (brief §40, steps 1-3). The actual
    /// MFE/MAE/Return/Hit math is never inlined here - it lives entirely in
    /// <see cref="ScientificMeasurementEngine"/> (brief §39: "Ne pas mettre MFE/MAE directement dans
    /// BacktestEngine").
    /// </summary>
    public BacktestMeasuredSignalPipelineResult RunMeasuredSignalPipeline(
        BacktestScenario scenario, int warmupBars, MeasurementConfiguration measurementConfiguration)
    {
        ArgumentNullException.ThrowIfNull(measurementConfiguration);

        BacktestSignalPipelineResult signalResult = RunSignalPipeline(scenario, warmupBars);
        IReadOnlyList<MeasurementResult> measurements =
            ScientificMeasurementEngine.MeasureAll(scenario.Series, signalResult.Bars, measurementConfiguration);

        return new BacktestMeasuredSignalPipelineResult(
            signalResult, measurements, MeasurementFingerprint.ComputeHash(measurements));
    }

    /// <summary>
    /// Sprint 15.25 (Lot 14.5, brief §36). Full chain: Signal (Lot 14.3, unchanged) -&gt; Measurement
    /// (Lot 14.4, unchanged) -&gt; Execution (Lot 14.5). Three strictly sequential, one-directional calls -
    /// each stage fully returns before the next begins, so nothing computed later can ever reach back and
    /// alter an earlier stage's output. Measurement and Execution never call into each other (brief §4/§35)
    /// - both are computed independently from the SAME <see cref="BacktestSignalPipelineResult"/>, then
    /// paired by <see cref="BacktestSimulationResult"/> without recomputing or duplicating fields.
    /// </summary>
    public BacktestSimulationResult RunSimulation(
        BacktestScenario scenario,
        int warmupBars,
        MeasurementConfiguration measurementConfiguration,
        ExecutionConfiguration executionConfiguration)
    {
        ArgumentNullException.ThrowIfNull(measurementConfiguration);
        ArgumentNullException.ThrowIfNull(executionConfiguration);

        BacktestSignalPipelineResult signalResult = RunSignalPipeline(scenario, warmupBars);
        IReadOnlyList<MeasurementResult> measurements =
            ScientificMeasurementEngine.MeasureAll(scenario.Series, signalResult.Bars, measurementConfiguration);
        BacktestExecutionResult executionResult =
            ExecutionSimulator.SimulateAll(scenario.Series, signalResult.Bars, executionConfiguration);

        return new BacktestSimulationResult(signalResult, measurements, executionResult);
    }

    /// <summary>
    /// Sprint 15.25 (Lot 14.6, brief §47). Full chain: Signal (Lot 14.3) -&gt; Measurement (Lot 14.4) -&gt;
    /// Execution (Lot 14.5) -&gt; P&amp;L (Lot 14.6). Four strictly sequential, one-directional calls; the
    /// P&amp;L stage itself never touches <paramref name="scenario"/>.Series or re-runs any earlier stage
    /// (brief §47: "Ne pas refaire les étapes précédentes dans le moteur P&amp;L") - it consumes exactly the
    /// <see cref="SimulatedPosition"/> list <see cref="ExecutionSimulator.SimulateAll"/> already produced.
    /// </summary>
    public BacktestFullResult RunFullBacktest(
        BacktestScenario scenario,
        int warmupBars,
        MeasurementConfiguration measurementConfiguration,
        ExecutionConfiguration executionConfiguration,
        PnLConfiguration pnlConfiguration)
    {
        ArgumentNullException.ThrowIfNull(measurementConfiguration);
        ArgumentNullException.ThrowIfNull(executionConfiguration);
        ArgumentNullException.ThrowIfNull(pnlConfiguration);

        BacktestSignalPipelineResult signalResult = RunSignalPipeline(scenario, warmupBars);
        IReadOnlyList<MeasurementResult> measurements =
            ScientificMeasurementEngine.MeasureAll(scenario.Series, signalResult.Bars, measurementConfiguration);
        BacktestExecutionResult executionResult =
            ExecutionSimulator.SimulateAll(scenario.Series, signalResult.Bars, executionConfiguration);
        IReadOnlyList<PositionPnLResult> positionPnLResults =
            PositionPnLCalculator.CalculateAll(executionResult.Positions, pnlConfiguration);
        BacktestPnLResult pnlResult = BacktestPnLResultBuilder.Build(positionPnLResults, pnlConfiguration.StartingCapital);

        return new BacktestFullResult(signalResult, measurements, executionResult, pnlResult);
    }

    /// <summary>
    /// Sprint 15.25 (Lot 14.7). Full chain: Signal -&gt; Measurement -&gt; Execution -&gt; Gross P&amp;L (Lot 14.6,
    /// via <see cref="RunFullBacktest"/>, completely unchanged) -&gt; Cost/Slippage/Execution-Realism (Lot
    /// 14.7). Purely additive: calls <see cref="RunFullBacktest"/> as-is and layers
    /// <see cref="PositionCostCalculator"/>/<see cref="BacktestCostResultBuilder"/> on top of its
    /// already-computed <see cref="SimulatedPosition"/>/<see cref="PositionPnLResult"/> lists - never
    /// recomputes Signal/Measurement/Execution/GrossPnL (Lot 14.7 brief §14: "L'intégration doit être
    /// additive").
    ///
    /// With <paramref name="costConfiguration"/>.Enabled == false (see
    /// <see cref="ExecutionCostConfiguration.Disabled"/>, the recommended default), every NetPnL equals its
    /// GrossPnL counterpart and the NetEquityCurve is numerically identical to
    /// <c>FullResult.PnLResult.EquityCurve</c> (Lot 14.7 brief §8/§9: "configuration par défaut =
    /// comportement Lot 14.6") - verified by the zero-cost-equivalence tests.
    /// </summary>
    public BacktestFullResultWithCosts RunFullBacktestWithCosts(
        BacktestScenario scenario,
        int warmupBars,
        MeasurementConfiguration measurementConfiguration,
        ExecutionConfiguration executionConfiguration,
        PnLConfiguration pnlConfiguration,
        ExecutionCostConfiguration costConfiguration)
    {
        ArgumentNullException.ThrowIfNull(costConfiguration);

        BacktestFullResult fullResult = RunFullBacktest(scenario, warmupBars, measurementConfiguration, executionConfiguration, pnlConfiguration);
        IReadOnlyList<PositionCostResult> positionCostResults = PositionCostCalculator.CalculateAll(
            fullResult.ExecutionResult.Positions, fullResult.PnLResult.PositionPnLResults, pnlConfiguration, costConfiguration);
        BacktestCostResult costResult = BacktestCostResultBuilder.Build(positionCostResults, pnlConfiguration.StartingCapital);

        return new BacktestFullResultWithCosts(fullResult, costResult);
    }

    /// <summary>
    /// Sprint 15.25 (Lot 14.8). Full chain: Signal -&gt; Measurement -&gt; Execution (Lots 14.3-14.5, unchanged)
    /// -&gt; Risk Evaluation -&gt; Position Sizing -&gt; Cost -&gt; PnL -&gt; Equity (Lot 14.8). Sibling to
    /// <see cref="RunFullBacktest"/>/<see cref="RunFullBacktestWithCosts"/> - calls
    /// <see cref="RunSignalPipeline"/>/<see cref="ScientificMeasurementEngine.MeasureAll"/>/
    /// <see cref="ExecutionSimulator.SimulateAll"/> exactly as those methods already do (Lot 14.8 brief
    /// §14: "L'intégration doit être additive"), then builds a risk-aware, per-position-quantity Cost/PnL/
    /// Equity result via <see cref="BacktestRiskResultBuilder"/> instead of the uniform-quantity
    /// <see cref="PositionPnLCalculator.CalculateAll"/>/<see cref="PositionCostCalculator.CalculateAll"/>
    /// path <see cref="RunFullBacktest"/>/<see cref="RunFullBacktestWithCosts"/> use (Lot 14.8 brief §15:
    /// quantity now varies per position, so those two list-level helpers - which apply ONE quantity to
    /// every position - are the wrong tool here; <see cref="BacktestRiskResultBuilder"/> instead calls the
    /// single-position <c>PositionPnLCalculator.Calculate</c>/<c>PositionCostCalculator.Calculate</c>
    /// overloads directly, once per position, with a per-position quantity-overridden
    /// <see cref="PnLConfiguration"/>).
    ///
    /// With <paramref name="riskConfiguration"/>.EnableRiskControls == false (see
    /// <see cref="BacktestRiskConfiguration.Disabled"/>, the recommended default), every position's
    /// AllowedQuantity equals <paramref name="pnlConfiguration"/>.Quantity and the resulting
    /// FinalNetPnL/MaximumDrawdown are numerically identical to <see cref="RunFullBacktestWithCosts"/>'s
    /// own FinalGrossPnL/MaximumDrawdown for the same scenario/pnlConfiguration/costConfiguration (Lot
    /// 14.8 brief §18) - verified by this lot's zero-regression tests.
    /// </summary>
    public BacktestFullResultWithRisk RunFullBacktestWithRisk(
        BacktestScenario scenario,
        int warmupBars,
        MeasurementConfiguration measurementConfiguration,
        ExecutionConfiguration executionConfiguration,
        PnLConfiguration pnlConfiguration,
        ExecutionCostConfiguration costConfiguration,
        BacktestRiskConfiguration riskConfiguration)
        => RunFullBacktestWithRisk(
            scenario, warmupBars, measurementConfiguration, executionConfiguration,
            pnlConfiguration, costConfiguration, riskConfiguration, PipelineParameterOverrides.None);

    /// <summary>
    /// Sprint 15.25 (Lot 14.10, P0-3). Identical to the seven-argument
    /// <see cref="RunFullBacktestWithRisk(BacktestScenario, int, MeasurementConfiguration, ExecutionConfiguration, PnLConfiguration, ExecutionCostConfiguration, BacktestRiskConfiguration)"/>
    /// in every respect except that <paramref name="overrides"/> is threaded into the underlying
    /// <see cref="RunSignalPipeline(BacktestScenario, int, PipelineParameterOverrides)"/> call - this is the
    /// end-to-end path <c>Backtest.Calibration.CalibrationExperimentRunner</c> uses to make a
    /// <c>CalibrationParameterSet</c> actually change pipeline behaviour (see
    /// <c>Backtest.Calibration.CalibrationParameterBinding</c>). The seven-argument overload above is the
    /// only thing every pre-Lot-14.10 caller ever sees, and it delegates here with
    /// <see cref="PipelineParameterOverrides.None"/> - bit-for-bit unchanged behaviour (brief's "RÈGLE DE
    /// NON-RÉGRESSION").
    /// </summary>
    public BacktestFullResultWithRisk RunFullBacktestWithRisk(
        BacktestScenario scenario,
        int warmupBars,
        MeasurementConfiguration measurementConfiguration,
        ExecutionConfiguration executionConfiguration,
        PnLConfiguration pnlConfiguration,
        ExecutionCostConfiguration costConfiguration,
        BacktestRiskConfiguration riskConfiguration,
        PipelineParameterOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(pnlConfiguration);
        ArgumentNullException.ThrowIfNull(costConfiguration);
        ArgumentNullException.ThrowIfNull(riskConfiguration);
        ArgumentNullException.ThrowIfNull(overrides);

        BacktestSignalPipelineResult signalResult = RunSignalPipeline(scenario, warmupBars, overrides);
        IReadOnlyList<MeasurementResult> measurements =
            ScientificMeasurementEngine.MeasureAll(scenario.Series, signalResult.Bars, measurementConfiguration);
        BacktestExecutionResult executionResult =
            ExecutionSimulator.SimulateAll(scenario.Series, signalResult.Bars, executionConfiguration);

        BacktestRiskResult riskResult = BacktestRiskResultBuilder.Build(
            executionResult.Positions, scenario.InitialCapital, scenario.Instrument, scenario.Policy,
            pnlConfiguration, costConfiguration, riskConfiguration);

        return new BacktestFullResultWithRisk(signalResult, measurements, executionResult, riskResult);
    }
}
