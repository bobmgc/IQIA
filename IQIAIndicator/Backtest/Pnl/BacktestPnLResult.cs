using System.Collections.Generic;

namespace IQIAIndicator.Backtest.Pnl;

/// <summary>
/// Sprint 15.25 (Lot 14.6, brief §48). Full result of the theoretical P&amp;L layer. Consolidates the
/// brief's "EquityCurve, DrawdownCurve" into ONE list (<see cref="EquityCurve"/>) whose
/// <see cref="EquityPoint.Drawdown"/> field already carries the drawdown series - two parallel arrays for
/// what is fundamentally one time series would only invite them to drift out of sync; documented here
/// rather than silently diverging from the brief's literal field list.
///
/// <see cref="BuySummary"/>/<see cref="SellSummary"/> (brief §19: BUY/SELL P&amp;L must stay separable) sit
/// alongside <see cref="Summary"/> (ALL positions) rather than nested inside it - all three share the
/// exact same <see cref="PnLSummary"/> shape, computed by the same code path over three different
/// populations.
/// </summary>
/// <param name="PositionPnLResults">One entry per input position, in the original (unsorted) order -
/// never re-ordered, unlike <see cref="EquityCurve"/> which IS chronologically ordered (brief §13).</param>
/// <param name="Summary">ALL positions.</param>
/// <param name="BuySummary">BUY_CANDIDATE positions only.</param>
/// <param name="SellSummary">SELL_CANDIDATE positions only.</param>
/// <param name="EquityCurve">Chronologically ordered (ExitTimestamp, then PositionId as a deterministic
/// tie-break - brief §13), one point per Closed position.</param>
/// <param name="FinalGrossPnL">The last point's CumulativePnL, or 0 when <see cref="EquityCurve"/> is
/// empty (brief §34 - never null, a P&amp;L of "nothing happened yet" is legitimately 0).</param>
/// <param name="MaximumDrawdown">The minimum Drawdown across <see cref="EquityCurve"/>, or 0 when empty
/// (brief §17/§34).</param>
/// <param name="StartingCapital">Echoes <see cref="PnLConfiguration.StartingCapital"/> - null means no
/// capital was supplied.</param>
/// <param name="FinalEquity">StartingCapital + FinalGrossPnL, only when StartingCapital was supplied.</param>
/// <param name="DeterministicHash">See <see cref="PnLFingerprint.ComputeHash"/>.</param>
public sealed record BacktestPnLResult(
    IReadOnlyList<PositionPnLResult> PositionPnLResults,
    PnLSummary Summary,
    PnLSummary BuySummary,
    PnLSummary SellSummary,
    IReadOnlyList<EquityPoint> EquityCurve,
    decimal FinalGrossPnL,
    decimal MaximumDrawdown,
    decimal? StartingCapital,
    decimal? FinalEquity,
    string DeterministicHash);
