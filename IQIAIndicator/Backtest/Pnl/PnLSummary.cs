namespace IQIAIndicator.Backtest.Pnl;

/// <summary>
/// Sprint 15.25 (Lot 14.6, brief §20/§21/§22/§23/§24). Aggregate P&amp;L statistics over one population of
/// <see cref="PositionPnLResult"/>, restricted to <see cref="Execution.PositionStatus.Closed"/> for every
/// field except <see cref="PositionCount"/> itself (mirrors Lot 14.4's
/// <c>ScientificMeasurementSummary</c>: a NotExecutable/InvalidEntry/InsufficientFutureData/InvalidExit
/// position is never silently averaged in as a zero).
///
/// "Gross" means BEFORE any cost (brief §20: never named NetProfit) - this lot implements no
/// commission/spread/slippage/funding at all (brief §26), so "Gross" and "the only number this lot
/// produces" happen to coincide, but the name is kept literal for when a future lot subtracts costs.
/// </summary>
/// <param name="PositionCount">Every position passed in, regardless of status.</param>
/// <param name="ClosedCount">Positions that reached Closed - the population every other field below is
/// computed over.</param>
/// <param name="GrossProfit">Sum of GrossPnL strictly greater than 0 (brief §21 - a break-even GrossPnL
/// of exactly 0 belongs to neither GrossProfit nor GrossLoss, though it still counts in ClosedCount and
/// in Average/MedianPnL).</param>
/// <param name="GrossLoss">Sum of GrossPnL strictly less than 0 - kept SIGNED (negative), never an
/// absolute value (brief §21: "Ne pas prendre la valeur absolue de GrossLoss").</param>
/// <param name="NetGrossPnL">GrossProfit + GrossLoss (GrossLoss already negative).</param>
/// <param name="WinningPositions">Closed positions with GrossPnL &gt; 0.</param>
/// <param name="LosingPositions">Closed positions with GrossPnL &lt; 0.</param>
/// <param name="WinRate">WinningPositions / ClosedCount - null when ClosedCount is 0 (brief §22, never a
/// division by zero).</param>
/// <param name="AveragePnL">NetGrossPnL / ClosedCount - null when ClosedCount is 0 (brief §23).</param>
/// <param name="MedianPnL">Median of GrossPnL over Closed positions - see
/// <see cref="BacktestPnLResultBuilder.Median"/> for the exact convention (brief §24).</param>
public sealed record PnLSummary(
    int PositionCount,
    int ClosedCount,
    decimal GrossProfit,
    decimal GrossLoss,
    decimal NetGrossPnL,
    int WinningPositions,
    int LosingPositions,
    double? WinRate,
    decimal? AveragePnL,
    decimal? MedianPnL);
