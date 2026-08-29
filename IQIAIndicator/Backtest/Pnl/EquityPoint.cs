using System;

namespace IQIAIndicator.Backtest.Pnl;

/// <summary>
/// Sprint 15.25 (Lot 14.6, brief §10/§15/§16). One realized theoretical P&amp;L event - always tied to a
/// REAL closed position's REAL <see cref="ExitTimestamp"/> (never DateTime.Now/UtcNow, never a fabricated
/// "bar zero" timestamp - see <see cref="BacktestPnLResultBuilder"/>'s doc comment for why
/// "Equity[0] = StartingCapital" from the brief's own worked examples does not require inventing a point
/// with no real timestamp).
///
/// NO INTRABAR MARK-TO-MARKET (brief §15): one point per CLOSED position, nothing for positions still
/// theoretically "open" between EntryTimestamp and ExitTimestamp - <see cref="BacktestPnLResultBuilder"/>
/// only ever reads <see cref="PositionPnLResult"/> instances whose Status is Closed.
/// </summary>
/// <param name="Timestamp">The closing position's real ExitTimestamp.</param>
/// <param name="PositionId">Traces back to <see cref="Backtest.Execution.SimulatedPosition.PositionId"/>.</param>
/// <param name="IncrementalPnL">This position's own <see cref="PositionPnLResult.GrossPnL"/>.</param>
/// <param name="CumulativePnL">Running sum of every IncrementalPnL up to and including this point, in
/// (Timestamp, PositionId) order (brief §13's deterministic tie-break).</param>
/// <param name="Equity">StartingCapital + CumulativePnL, only when
/// <see cref="PnLConfiguration.StartingCapital"/> was supplied (brief §12); null otherwise - never
/// confused with <see cref="CumulativePnL"/> (brief §12: "CumulativePnL ≠ EquityCurve").</param>
/// <param name="Drawdown">CumulativePnL minus the running peak of CumulativePnL up to this point - always
/// &lt;= 0 (brief §16). Mathematically identical to "Equity minus running Peak Equity" (a constant
/// StartingCapital offset cancels out of a difference) - computed this way so Drawdown/MaximumDrawdown are
/// available even with no StartingCapital, verified against the brief's own worked examples (§17/§37/§38).</param>
/// <param name="DrawdownPercent">Drawdown / PeakEquity - only when Equity (StartingCapital) is available,
/// since a percentage requires a real capital level, not just a running peak of a relative curve.</param>
public sealed record EquityPoint(
    DateTime Timestamp,
    int PositionId,
    decimal IncrementalPnL,
    decimal CumulativePnL,
    decimal? Equity,
    decimal Drawdown,
    double? DrawdownPercent);
