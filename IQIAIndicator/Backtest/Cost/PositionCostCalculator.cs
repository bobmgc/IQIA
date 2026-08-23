using System;
using System.Collections.Generic;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Pnl;

namespace IQIAIndicator.Backtest.Cost;

/// <summary>
/// Sprint 15.25 (Lot 14.7, brief §3/§7/§8). Converts one <see cref="SimulatedPosition"/> (Lot 14.5,
/// unmodified) plus its already-computed <see cref="PositionPnLResult"/> (Lot 14.6, unmodified) into a
/// <see cref="PositionCostResult"/>, using a caller-supplied <see cref="ExecutionCostConfiguration"/>.
/// Never re-runs the signal/measurement/execution/P&amp;L pipeline - the only inputs read are the
/// already-resolved fields of both source records plus the two configurations.
///
/// STATELESS BY DESIGN: static class, no fields.
///
/// FORMULA (brief §7/§8), for a Closed position with quantity Q, PriceUnitValue V (from
/// <see cref="PnLConfiguration.Instrument"/>), full spread S and slippage K (both in price units):
/// <code>
/// adverse       = S/2 + K                                   (price units, per leg)
/// ExecutedEntry = TheoreticalEntry +/- adverse               (sign = entry fill side)
/// ExecutedExit  = TheoreticalExit  +/- adverse               (sign = exit fill side)
/// SpreadCost    = 2 * (S/2) * V * Q  = S * V * Q             (both legs)
/// SlippageCost  = 2 * K * V * Q                              (both legs)
/// Commission    = 2 * PerOrder + 2 * Q * PerUnit             (both legs)
/// Fees          = 2 * PerOrder                               (both legs)
/// TotalCost     = SpreadCost + SlippageCost + Commission + Fees
/// NetPnL        = GrossPnL - TotalCost
/// </code>
/// This is exactly equivalent to computing PnL directly from executed prices and then subtracting
/// Commission+Fees only (SpreadCost/SlippageCost are already embedded in the executed-price difference) -
/// verified by <c>PositionCostCalculatorTests</c>'s manual-calculation tests, so nothing is double-counted
/// (brief §7).
///
/// With <see cref="ExecutionCostConfiguration.Enabled"/> == false: Executed* == Theoretical*, every cost
/// component is exactly 0, and NetPnL == GrossPnL (brief §8/§9's zero-cost/disabled equivalence to Lot
/// 14.6) - a real short-circuit, not merely the natural result of zero-valued sub-configurations.
/// </summary>
public static class PositionCostCalculator
{
    public static PositionCostResult Calculate(
        SimulatedPosition position,
        PositionPnLResult pnlResult,
        PnLConfiguration pnlConfiguration,
        ExecutionCostConfiguration costConfiguration)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(pnlResult);
        ArgumentNullException.ThrowIfNull(pnlConfiguration);
        ArgumentNullException.ThrowIfNull(costConfiguration);

        if (position.Status != PositionStatus.Closed
            || position.EntryPrice is not decimal theoreticalEntry
            || position.ExitPrice is not decimal theoreticalExit
            || position.ExitTimestamp is not DateTime exitTimestamp
            || pnlResult.GrossPnL is not decimal grossPnL)
        {
            return new PositionCostResult(
                position.PositionId, position.Status, position.Direction,
                position.EntryTimestamp, position.EntryPrice, null, position.ExitTimestamp, position.ExitPrice, null,
                null, null, null,
                pnlConfiguration.Instrument.Currency, pnlConfiguration.Quantity);
        }

        int quantity = pnlConfiguration.Quantity;
        decimal priceUnitValue = pnlConfiguration.Instrument.PriceUnitValue;

        if (!costConfiguration.Enabled)
        {
            var zeroCost = new ExecutionCost(0m, 0m, 0m, 0m, 0m);
            return new PositionCostResult(
                position.PositionId, position.Status, position.Direction,
                position.EntryTimestamp, theoreticalEntry, theoreticalEntry, exitTimestamp, theoreticalExit, theoreticalExit,
                grossPnL, zeroCost, grossPnL,
                pnlConfiguration.Instrument.Currency, quantity);
        }

        OrderSide entrySide = ExecutionPriceModel.EntryFillSide(position.Direction);
        OrderSide exitSide = ExecutionPriceModel.ExitFillSide(position.Direction);

        ExecutedPrice entryFill = ExecutionPriceModel.Apply(
            theoreticalEntry, entrySide, quantity, position.EntryTimestamp, costConfiguration.Slippage, costConfiguration.Spread);
        ExecutedPrice exitFill = ExecutionPriceModel.Apply(
            theoreticalExit, exitSide, quantity, exitTimestamp, costConfiguration.Slippage, costConfiguration.Spread);

        decimal spreadCost = (entryFill.SpreadApplied + exitFill.SpreadApplied) * priceUnitValue * quantity;
        decimal slippageCost = (entryFill.SlippageApplied + exitFill.SlippageApplied) * priceUnitValue * quantity;
        decimal commission = 2m * costConfiguration.Commission.PerOrder + 2m * quantity * costConfiguration.Commission.PerUnit;
        decimal fees = 2m * costConfiguration.Fees.PerOrder;
        decimal totalCost = commission + fees + spreadCost + slippageCost;
        decimal netPnL = grossPnL - totalCost;

        var cost = new ExecutionCost(commission, fees, spreadCost, slippageCost, totalCost);

        return new PositionCostResult(
            position.PositionId, position.Status, position.Direction,
            position.EntryTimestamp, theoreticalEntry, entryFill.Price, exitTimestamp, theoreticalExit, exitFill.Price,
            grossPnL, cost, netPnL,
            pnlConfiguration.Instrument.Currency, quantity);
    }

    /// <summary>
    /// Brief §14: <paramref name="positions"/>[i] and <paramref name="pnlResults"/>[i] must already be
    /// index-aligned by PositionId - exactly the invariant <see cref="Pnl.BacktestFullResult"/>'s own doc
    /// comment documents between <c>ExecutionResult.Positions</c> and <c>PnLResult.PositionPnLResults</c>.
    /// Never recomputed or re-paired here.
    /// </summary>
    public static IReadOnlyList<PositionCostResult> CalculateAll(
        IReadOnlyList<SimulatedPosition> positions,
        IReadOnlyList<PositionPnLResult> pnlResults,
        PnLConfiguration pnlConfiguration,
        ExecutionCostConfiguration costConfiguration)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(pnlResults);
        ArgumentNullException.ThrowIfNull(pnlConfiguration);
        ArgumentNullException.ThrowIfNull(costConfiguration);

        if (positions.Count != pnlResults.Count)
            throw new ArgumentException("Positions and PnL results must be the same length and index-aligned by PositionId.", nameof(pnlResults));

        var results = new List<PositionCostResult>(positions.Count);
        for (int i = 0; i < positions.Count; i++)
            results.Add(Calculate(positions[i], pnlResults[i], pnlConfiguration, costConfiguration));

        return results.AsReadOnly();
    }
}
