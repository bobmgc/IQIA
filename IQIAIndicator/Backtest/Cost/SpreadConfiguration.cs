using System;

namespace IQIAIndicator.Backtest.Cost;

/// <summary>
/// Sprint 15.25 (Lot 14.7, brief §6). Deterministic FULL bid/ask spread width, in PRICE UNITS (never
/// dollars). Brief §6's symmetric convention - <c>ask = mid + spread/2</c>, <c>bid = mid - spread/2</c> -
/// is applied by <see cref="ExecutionPriceModel"/>, not here; this type only ever resolves and validates
/// the single <see cref="PriceUnits"/> width, exactly like <see cref="SlippageConfiguration"/>.
///
/// Brief §6: "Ne pas prétendre simuler un spread réel si les données disponibles ne contiennent pas
/// Bid/Ask" - this lot's series carries no real Bid/Ask, so every spread used here is an explicitly
/// CONFIGURED synthetic width, never derived from data the pipeline does not have.
/// </summary>
public sealed record SpreadConfiguration
{
    /// <summary>Full spread width (ask - bid), always &gt;= 0.</summary>
    public required decimal PriceUnits { get; init; }

    /// <summary>Brief §6 "zero spread".</summary>
    public static SpreadConfiguration None() => new() { PriceUnits = 0m };

    /// <summary>Brief §6 "symmetric spread" - e.g. <c>Fixed(0.5m)</c> means +/-0.25 around mid.</summary>
    public static SpreadConfiguration Fixed(decimal priceUnits)
    {
        if (priceUnits < 0m)
            throw new ArgumentOutOfRangeException(nameof(priceUnits), priceUnits, "Spread cannot be negative.");

        return new SpreadConfiguration { PriceUnits = priceUnits };
    }

    /// <summary>Same tick-conversion convention as <see cref="SlippageConfiguration.FromTicks"/>.</summary>
    public static SpreadConfiguration FromTicks(decimal ticks, decimal tickSize)
    {
        if (ticks < 0m)
            throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "Spread ticks cannot be negative.");

        if (tickSize <= 0m)
            throw new ArgumentOutOfRangeException(nameof(tickSize), tickSize, "TickSize must be strictly positive.");

        return new SpreadConfiguration { PriceUnits = ticks * tickSize };
    }
}
