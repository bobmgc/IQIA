using System;
using System.Collections.Generic;
using IQIAIndicator.Backtest.Execution;

namespace IQIAIndicator.Backtest.Pnl;

/// <summary>
/// Sprint 15.25 (Lot 14.6, brief §3/§7). Converts a <see cref="SimulatedPosition"/> (Lot 14.5, unmodified)
/// into a <see cref="PositionPnLResult"/> using a caller-supplied <see cref="PnLConfiguration"/>. Never
/// re-runs the signal/measurement/execution pipeline (brief §47: "Ne pas refaire les étapes précédentes
/// dans le moteur P&amp;L") - the ONLY inputs this class reads are the already-resolved
/// <see cref="SimulatedPosition"/> fields and the configuration.
///
/// STATELESS BY DESIGN (brief §33): static class, no fields.
/// </summary>
public static class PositionPnLCalculator
{
    /// <summary>
    /// Brief §7: <c>GrossPnL = GrossPriceMove x PriceUnitValue x Quantity</c>. Sign is inherited entirely
    /// from <see cref="SimulatedPosition.GrossPriceMove"/> (already signed favorable-positive by Lot
    /// 14.5's own convention) - this method never re-derives BUY/SELL sign logic itself.
    /// Null when the source position never reached <see cref="PositionStatus.Closed"/> (no price move to
    /// convert) - never a fabricated zero.
    /// </summary>
    public static PositionPnLResult Calculate(SimulatedPosition position, PnLConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(configuration);

        decimal? grossPnL = position.GrossPriceMove is decimal move
            ? move * configuration.Instrument.PriceUnitValue * configuration.Quantity
            : null;

        return new PositionPnLResult(
            position.PositionId,
            position.Status,
            position.Direction,
            position.EntryTimestamp,
            position.EntryPrice,
            position.ExitTimestamp,
            position.ExitPrice,
            position.HoldingBars,
            position.GrossPriceMove,
            position.Return,
            grossPnL,
            configuration.Instrument.Currency,
            configuration.Quantity,
            position.ExitReason);
    }

    public static IReadOnlyList<PositionPnLResult> CalculateAll(IReadOnlyList<SimulatedPosition> positions, PnLConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(configuration);

        var results = new List<PositionPnLResult>(positions.Count);
        foreach (SimulatedPosition position in positions)
            results.Add(Calculate(position, configuration));

        return results.AsReadOnly();
    }
}
