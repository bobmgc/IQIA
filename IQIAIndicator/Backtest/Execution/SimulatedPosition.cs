using System;
using IQIAIndicator.Engine.EntryTrigger;

namespace IQIAIndicator.Backtest.Execution;

/// <summary>
/// Sprint 15.25 (Lot 14.5, brief §16). Every reason a candidate at bar i did not become a closed
/// theoretical position - same non-swallowing discipline as Lot 14.4's <c>MeasurementStatus</c>. No
/// separate "EXECUTED" status: this engine resolves entry and exit in ONE synchronous, deterministic
/// call (brief §41: no live/streaming state), so an "entry accepted but not yet closed" state can never
/// actually be observed here - documented explicitly rather than adding a status value nothing can ever
/// produce.
/// </summary>
public enum PositionStatus
{
    /// <summary>Entry and exit both resolved. Every Entry*/Exit*/HoldingBars/GrossPriceMove/Return field
    /// is populated.</summary>
    Closed,

    /// <summary>No TradePlan was produced, or Direction is not BUY_CANDIDATE/SELL_CANDIDATE (brief §7/§19
    /// - NO_ACTION/WATCH never create a position).</summary>
    NotExecutable,

    /// <summary>Sprint 15.25 (Lot 14.10, P0-1): either (a) Candidate.EntryPrice (the signal bar's own
    /// TradePlan reference price) is missing or not strictly positive - a data-quality gate on the signal
    /// bar itself, checked before any fill is attempted - or (b) the fill bar (SignalBarIndex+1) itself
    /// failed <c>HistoricalBar.Validate()</c> or its Open is not strictly positive. Both cases mean "no
    /// honest entry price could be established", never a silent fallback to another field. Decimal cannot
    /// represent NaN/Infinity, so the NaN/Infinity half of this check is structurally unreachable - documented
    /// rather than defended against dead code.</summary>
    InvalidEntry,

    /// <summary>Sprint 15.25 (Lot 14.10, P0-1): either (a) SignalBarIndex+1 (the fill bar the corrected
    /// entry convention requires) does not exist yet, or (b) EntryBarIndex + HorizonBars exceeds the
    /// series' last index (brief §18 of Lot 14.5) - the exit bar the TIME_HORIZON convention requires does
    /// not exist yet. Never truncated to an earlier bar in either case.</summary>
    InsufficientFutureData,

    /// <summary>The exit bar itself fails <c>HistoricalBar.Validate()</c> (brief §21) - unreachable
    /// through a <see cref="HistoricalSeries"/> built via its own validating constructors (same situation
    /// as Lot 14.1's BarsRejected / Lot 14.4's InvalidFutureData), kept as defence in depth and exercised
    /// directly against a hand-built bar list in tests. Sprint 15.25 (Lot 15.4): also returned when any
    /// bar being intrabar-monitored for Stop/Target (not just the final horizon bar) fails validation.
    InvalidExit,

    /// <summary>Sprint 15.25 (Lot 15.4, brief §5): <c>ExecutionCandidate.StopLoss</c>/<c>.TakeProfit</c>
    /// is present but violates the direction invariant (BUY requires StopLoss &lt; EntryPrice &lt;
    /// TakeProfit; SELL the mirror) - never executed silently. Defense in depth: <c>TradePlanBuilder</c>
    /// and <c>VolatilityStopLossModel</c> (both unmodified, upstream) already guarantee this today: this
    /// status exists so a future change to either could never silently reach a fabricated position here.</summary>
    InvalidStopTarget
}

/// <summary>Sprint 15.25 (Lot 14.5, brief §17). Why a CLOSED position closed. Originally a single member
/// (TIME_HORIZON only, no Stop Loss/Take Profit in that lot); Sprint 15.25 (Lot 15.4) adds the three
/// members that lot's own doc comment reserved space for, without changing SimulatedPosition's shape
/// (ExitPrice/ExitBarIndex/ExitTimestamp already existed and are reused unchanged, brief §26).</summary>
public enum ExitReason
{
    TimeHorizon,

    /// <summary>Sprint 15.25 (Lot 15.4): intrabar Low (BUY) / High (SELL) touched
    /// <c>ExecutionCandidate.StopLoss</c> on this bar, and TakeProfit was not ALSO touched the same bar
    /// (see <see cref="Ambiguous"/> otherwise).</summary>
    StopLoss,

    /// <summary>Sprint 15.25 (Lot 15.4): intrabar High (BUY) / Low (SELL) touched
    /// <c>ExecutionCandidate.TakeProfit</c> on this bar, and StopLoss was not ALSO touched the same bar
    /// (see <see cref="Ambiguous"/> otherwise).</summary>
    TakeProfit,

    /// <summary>Sprint 15.25 (Lot 15.4, brief §7/§8/§9): the SAME bar's High and Low touched BOTH
    /// StopLoss and TakeProfit - single-timeframe OHLC cannot determine which was actually hit first.
    /// Never resolved by an invented assumption. <see cref="SimulatedPosition.ExitPrice"/> on an
    /// Ambiguous exit uses the conservative convention (the StopLoss price, i.e. the outcome unfavorable
    /// to the trade) for measurement usability ONLY - this is never a claim about the true execution
    /// order, and a consumer that wants a different convention can filter on this ExitReason precisely
    /// because it is never conflated with a genuine <see cref="StopLoss"/> exit.</summary>
    Ambiguous
}

/// <summary>
/// Sprint 15.25 (Lot 14.5, brief §2/§6). A deterministic THEORETICAL position - never an order, never a
/// broker fill (brief §2: "Elle ne doit pas envoyer d'ordre"). Immutable by construction (a
/// <c>sealed record</c>, every property <c>init</c>-only via the positional constructor) - brief §6: "Une
/// position fermée doit être immuable"; since this engine resolves a position atomically in one call
/// (see <see cref="PositionStatus"/>'s doc comment), EVERY SimulatedPosition this lot ever produces is
/// already in its final, immutable state the moment it is constructed - there is no mutation path to
/// forbid.
///
/// <see cref="PositionId"/> is the candidate's own <see cref="ExecutionCandidate.SignalBarIndex"/> - this
/// lot has exactly one candidate per signal bar (brief §31: "chaque signal doit être traité comme une
/// position théorique indépendante"), so the bar index is already a unique, deterministic identifier.
/// Never a <c>Guid.NewGuid()</c>, never a mutable counter.
/// </summary>
public sealed record SimulatedPosition(
    int PositionId,
    PositionStatus Status,
    string? Reason,
    DirectionCandidate Direction,
    DateTime EntryTimestamp,
    decimal? EntryPrice,
    int EntryBarIndex,
    DateTime? ExitTimestamp,
    decimal? ExitPrice,
    int? ExitBarIndex,
    ExitReason? ExitReason,
    int? HoldingBars,
    decimal? GrossPriceMove,
    double? Return);
