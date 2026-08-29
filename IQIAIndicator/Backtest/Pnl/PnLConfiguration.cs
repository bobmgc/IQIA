using System;

namespace IQIAIndicator.Backtest.Pnl;

/// <summary>
/// Sprint 15.25 (Lot 14.6, brief §5/§11). Simulation-level configuration for the theoretical P&amp;L layer.
/// </summary>
public sealed record PnLConfiguration
{
    public required InstrumentPnLSpecification Instrument { get; init; }

    /// <summary>Theoretical unit count, default 1 (brief §5). NOT a Risk Engine output, NOT a trading
    /// recommendation - "TheoreticalQuantity is a simulation parameter, not a Risk Engine output" (brief
    /// §5, reproduced verbatim here as the contract this field exists to honour).</summary>
    public required int Quantity { get; init; }

    /// <summary>Theoretical starting capital - never connected to ATAS/RiskCurrentEquity/AccountState
    /// (brief §11). Null means "no capital was supplied" (brief §12): the engine then produces a
    /// CumulativePnL curve without inventing one, and <see cref="BacktestPnlResult.FinalEquity"/> stays
    /// null too.</summary>
    public decimal? StartingCapital { get; init; }

    public static PnLConfiguration Create(InstrumentPnLSpecification instrument, int quantity = 1, decimal? startingCapital = null)
    {
        ArgumentNullException.ThrowIfNull(instrument);

        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "TheoreticalQuantity must be strictly positive.");

        if (startingCapital is decimal capital && capital <= 0m)
            throw new ArgumentOutOfRangeException(nameof(startingCapital), capital, "StartingCapital, when supplied, must be strictly positive.");

        return new PnLConfiguration { Instrument = instrument, Quantity = quantity, StartingCapital = startingCapital };
    }
}
