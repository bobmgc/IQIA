using System;
using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Backtest.Execution;

namespace IQIAIndicator.Backtest.Cost;

/// <summary>
/// Sprint 15.25 (Lot 14.7). Builds the net equity curve, net drawdown series and cost totals from a
/// population of <see cref="PositionCostResult"/> - the exact structural mirror of
/// <see cref="Pnl.BacktestPnLResultBuilder"/> (Lot 14.6, unmodified), substituting NetPnL for GrossPnL.
/// Stateless (static class, no fields).
///
/// Running peak starts at 0 (the implicit "Equity[0] = StartingCapital" baseline) - identical convention
/// to Lot 14.6, so that when every <see cref="PositionCostResult.NetPnL"/> equals its GrossPnL (zero-cost
/// or costs-disabled), this method reproduces <see cref="Pnl.BacktestPnLResultBuilder.Build"/>'s numbers
/// EXACTLY, point for point (brief §8/§9's mandatory equivalence).
/// </summary>
public static class BacktestCostResultBuilder
{
    public static BacktestCostResult Build(IReadOnlyList<PositionCostResult> positionCostResults, decimal? startingCapital)
    {
        ArgumentNullException.ThrowIfNull(positionCostResults);

        if (startingCapital is decimal capital && capital <= 0m)
            throw new ArgumentOutOfRangeException(nameof(startingCapital), capital, "StartingCapital, when supplied, must be strictly positive.");

        // Same deterministic total order as BacktestPnLResultBuilder: ExitTimestamp, then PositionId.
        List<PositionCostResult> closed = positionCostResults
            .Where(p => p.Status == PositionStatus.Closed)
            .OrderBy(p => p.ExitTimestamp!.Value)
            .ThenBy(p => p.PositionId)
            .ToList();

        var netEquityCurve = new List<NetEquityPoint>(closed.Count);
        decimal cumulative = 0m;
        decimal peak = 0m; // implicit "Equity[0]" baseline - see class doc comment.
        decimal totalCommission = 0m, totalFees = 0m, totalSpreadCost = 0m, totalSlippageCost = 0m;

        foreach (PositionCostResult position in closed)
        {
            decimal incremental = position.NetPnL!.Value;
            cumulative += incremental;
            if (cumulative > peak)
                peak = cumulative;

            decimal drawdown = cumulative - peak;
            decimal? equity = startingCapital is decimal sc ? sc + cumulative : null;
            double? drawdownPercent = startingCapital is decimal sc2
                ? (double)(drawdown / (sc2 + peak))
                : null;

            netEquityCurve.Add(new NetEquityPoint(
                position.ExitTimestamp!.Value, position.PositionId, incremental, cumulative, equity, drawdown, drawdownPercent));

            ExecutionCost cost = position.Cost!;
            totalCommission += cost.Commission;
            totalFees += cost.Fees;
            totalSpreadCost += cost.SpreadCost;
            totalSlippageCost += cost.SlippageCost;
        }

        decimal finalNetPnL = netEquityCurve.Count > 0 ? netEquityCurve[^1].CumulativeNetPnL : 0m;
        decimal maximumNetDrawdown = netEquityCurve.Count > 0 ? netEquityCurve.Min(point => point.NetDrawdown) : 0m;
        decimal? finalNetEquity = startingCapital is decimal startingForFinal ? startingForFinal + finalNetPnL : null;
        decimal totalCost = totalCommission + totalFees + totalSpreadCost + totalSlippageCost;

        return new BacktestCostResult(
            positionCostResults,
            totalCommission, totalFees, totalSpreadCost, totalSlippageCost, totalCost,
            netEquityCurve.AsReadOnly(),
            finalNetPnL, maximumNetDrawdown, startingCapital, finalNetEquity,
            CostFingerprint.ComputeHash(netEquityCurve));
    }
}
