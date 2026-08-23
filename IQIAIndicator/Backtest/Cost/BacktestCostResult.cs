using System.Collections.Generic;

namespace IQIAIndicator.Backtest.Cost;

/// <summary>
/// Sprint 15.25 (Lot 14.7). Full result of the cost/execution-realism layer, built by
/// <see cref="BacktestCostResultBuilder"/> - the net-of-cost twin of <see cref="Pnl.BacktestPnLResult"/>
/// (Lot 14.6, unmodified). Returned alongside the untouched <see cref="Pnl.BacktestFullResult"/> by
/// <see cref="BacktestFullResultWithCosts"/>, never replacing it.
/// </summary>
/// <param name="PositionCostResults">One entry per input position, in the original (unsorted) order - same
/// convention as <c>BacktestPnLResult.PositionPnLResults</c>.</param>
/// <param name="TotalCommission">Sum of <see cref="ExecutionCost.Commission"/> over Closed positions.</param>
/// <param name="TotalFees">Sum of <see cref="ExecutionCost.Fees"/> over Closed positions.</param>
/// <param name="TotalSpreadCost">Sum of <see cref="ExecutionCost.SpreadCost"/> over Closed positions.</param>
/// <param name="TotalSlippageCost">Sum of <see cref="ExecutionCost.SlippageCost"/> over Closed positions.</param>
/// <param name="TotalCost">TotalCommission + TotalFees + TotalSpreadCost + TotalSlippageCost - always
/// exactly their sum, never independently computed.</param>
/// <param name="NetEquityCurve">Chronologically ordered (ExitTimestamp, then PositionId), one point per
/// Closed position - mirrors <c>BacktestPnLResult.EquityCurve</c>'s ordering exactly.</param>
/// <param name="FinalNetPnL">The last point's CumulativeNetPnL, or 0 when empty.</param>
/// <param name="MaximumNetDrawdown">The minimum NetDrawdown across <see cref="NetEquityCurve"/>, or 0 when
/// empty.</param>
/// <param name="StartingCapital">Echoes <see cref="Pnl.PnLConfiguration.StartingCapital"/>.</param>
/// <param name="FinalNetEquity">StartingCapital + FinalNetPnL, only when StartingCapital was supplied.</param>
/// <param name="DeterministicHash">See <see cref="CostFingerprint.ComputeHash"/>.</param>
public sealed record BacktestCostResult(
    IReadOnlyList<PositionCostResult> PositionCostResults,
    decimal TotalCommission,
    decimal TotalFees,
    decimal TotalSpreadCost,
    decimal TotalSlippageCost,
    decimal TotalCost,
    IReadOnlyList<NetEquityPoint> NetEquityCurve,
    decimal FinalNetPnL,
    decimal MaximumNetDrawdown,
    decimal? StartingCapital,
    decimal? FinalNetEquity,
    string DeterministicHash);
