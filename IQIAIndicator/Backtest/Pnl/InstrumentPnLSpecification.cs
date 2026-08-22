using System;

namespace IQIAIndicator.Backtest.Pnl;

/// <summary>
/// Sprint 15.25 (Lot 14.6, brief §4/§6). Explicit, caller-supplied price-to-money conversion for the
/// THEORETICAL backtest P&amp;L layer only. Never used by RiskEngine, never hardcoded per symbol inside the
/// engine (brief §4: "NE PAS hardcoder MES = $5/point, ES = $50/point dans le moteur") - MES/ES values
/// only ever appear as TEST FIXTURES (see Tests/Backtest/Pnl), never as an <c>if (symbol == ...)</c>
/// branch anywhere in this namespace (verified by grep).
/// </summary>
public sealed record InstrumentPnLSpecification
{
    public required string Symbol { get; init; }

    /// <summary>Monetary value of one price unit - e.g. 5 USD/point for MES, 50 USD/point for ES. A
    /// SIMULATION parameter, supplied by the caller, never derived from ATAS/RiskEngine.</summary>
    public required decimal PriceUnitValue { get; init; }

    public required string Currency { get; init; }

    /// <summary>Validates and builds. Throws rather than silently accepting a specification that could
    /// only ever produce a fabricated P&amp;L (brief §43: "Ne jamais produire un P&amp;L silencieusement
    /// faux").</summary>
    public static InstrumentPnLSpecification Create(string symbol, decimal priceUnitValue, string currency)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol is required.", nameof(symbol));

        if (priceUnitValue <= 0m)
            throw new ArgumentOutOfRangeException(nameof(priceUnitValue), priceUnitValue, "PriceUnitValue must be strictly positive.");

        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required - no FX conversion exists in this lot, so it is never defaulted.", nameof(currency));

        return new InstrumentPnLSpecification { Symbol = symbol, PriceUnitValue = priceUnitValue, Currency = currency };
    }
}
