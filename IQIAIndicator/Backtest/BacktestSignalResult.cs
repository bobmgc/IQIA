using IQIAIndicator.Core;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Entry;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.Presentation;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.TradePlan;

namespace IQIAIndicator.Backtest;

/// <summary>
/// Sprint 15.25 (Lot 14.3, brief §16/§28). Per-bar status of the full signal pipeline. Exactly one of
/// these four applies to every bar in <see cref="HistoricalSeries"/> - never both "warmup" and
/// "exception", never a bar silently missing from <see cref="BacktestSignalPipelineResult.Bars"/>.
/// </summary>
public enum BacktestSignalStatus
{
    /// <summary>MarketContextValidator rejected this bar's context - mirrors
    /// <see cref="BacktestFoundationResult.BarsRejected"/> (Lot 14.1). The pipeline never runs for this
    /// bar; every stage field below is null.</summary>
    Rejected,

    /// <summary>Bar index is below the caller-supplied <c>warmupBars</c> threshold (brief §5 - a
    /// Backtest-level label, not a new engine concept; the underlying engines still ran and may report
    /// their own NOT_READY/invalid evidence independently). The full pipeline still ran end to end - all
    /// stage fields are populated exactly as for <see cref="Ready"/>.</summary>
    Warmup,

    /// <summary>Bar index is at or past the warmup threshold and every stage completed without throwing.
    /// The stage results themselves may still individually report NOT_READY/NO_ACTION/SIGNAL_ONLY etc. -
    /// this status only means "the pipeline ran to completion for this bar", not "a trade was found".</summary>
    Ready,

    /// <summary>A pipeline stage threw for this bar (brief §18). <see cref="BacktestSignalResult.Exception"/>
    /// records which stage, the exception type and its message. Stages already completed before the
    /// throw keep their results (e.g. Regime/Decision may be populated even if Signal threw); everything
    /// from the failing stage onward is null. The run itself continues to the next bar - see
    /// <see cref="BacktestEngine.RunSignalPipeline"/>'s doc comment for why this is not escalated to a
    /// FAILED run.</summary>
    Exception
}

/// <summary>Records exactly which stage failed for one bar, and how - never a swallowed exception
/// (brief §18: "NE PAS faire catch { return null; } sans trace").</summary>
public sealed record BacktestStageException(
    string Stage,
    string ExceptionType,
    string Message);

/// <summary>
/// Sprint 15.25 (Lot 14.3, brief §16). One bar's full traversal of the IQIA signal pipeline:
/// MarketContext -&gt; Regime -&gt; Decision -&gt; Methodology -&gt; Signal -&gt; Entry -&gt; EntryTrigger -&gt; TradePlan.
///
/// Deliberately holds references to the REAL objects the real engines produced (brief §16: "Ne pas
/// copier inutilement tous les objets internes") - never a re-serialized/duplicated copy of their
/// fields. A null field means "this stage was never reached for this bar" (see
/// <see cref="Status"/>/<see cref="Reason"/> for why), not "this stage produced nothing" - every reached
/// stage in this pipeline always returns a non-null result (see <see cref="BacktestEngine"/>'s doc
/// comment on why TradePlan is the only stage with a legitimate "not reached" branch even on a bar with
/// no exception).
/// </summary>
public sealed record BacktestSignalResult(
    int BarIndex,
    DateTime Timestamp,
    BacktestSignalStatus Status,
    string? Reason,
    MarketContext? Context,
    EvidenceSet? Regime,
    DecisionResult? Decision,
    MethodologySelection? Methodology,
    OpportunityPresentation? Signal,
    EntryCandidate? Entry,
    EntryTriggerCandidate? EntryTrigger,
    TradePlan? TradePlan,
    BacktestStageException? Exception);
