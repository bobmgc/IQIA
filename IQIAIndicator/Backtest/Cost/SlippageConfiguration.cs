using System;

namespace IQIAIndicator.Backtest.Cost;

/// <summary>
/// Sprint 15.25 (Lot 14.7, brief §5). Deterministic slippage amount, expressed in PRICE UNITS (the same
/// unit as <see cref="Execution.SimulatedPosition.EntryPrice"/>/<c>ExitPrice</c> - points, not dollars).
/// Never random, never time-dependent (brief §11): the three ways to build one below (<see cref="None"/>,
/// <see cref="Fixed"/>, <see cref="FromTicks"/>) are just three ways to ARRIVE at the same single resolved
/// <see cref="PriceUnits"/> value - there is no runtime "mode" branch anywhere downstream.
/// </summary>
public sealed record SlippageConfiguration
{
    /// <summary>Always &gt;= 0 (brief §12: negative slippage is rejected, never silently clamped).</summary>
    public required decimal PriceUnits { get; init; }

    /// <summary>Brief §5 "Aucun slippage".</summary>
    public static SlippageConfiguration None() => new() { PriceUnits = 0m };

    /// <summary>Brief §5 "Slippage fixe" - e.g. <c>Fixed(0.25m)</c>.</summary>
    public static SlippageConfiguration Fixed(decimal priceUnits)
    {
        if (priceUnits < 0m)
            throw new ArgumentOutOfRangeException(nameof(priceUnits), priceUnits, "Slippage cannot be negative.");

        return new SlippageConfiguration { PriceUnits = priceUnits };
    }

    /// <summary>
    /// Brief §5 "Slippage en points/ticks": resolves <paramref name="ticks"/> * <paramref name="tickSize"/>
    /// once, at construction time, using the instrument's OWN tick size (brief §10 - never a hardcoded
    /// per-symbol constant). <paramref name="tickSize"/> is expected to come from the caller's existing
    /// <c>Core.InstrumentInfo.TickSize</c> or <c>Engine.Risk.InstrumentRiskSpecification.TickSize</c> -
    /// this type does not depend on either, to stay reusable for any instrument shape.
    /// </summary>
    public static SlippageConfiguration FromTicks(decimal ticks, decimal tickSize)
    {
        if (ticks < 0m)
            throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "Slippage ticks cannot be negative.");

        if (tickSize <= 0m)
            throw new ArgumentOutOfRangeException(nameof(tickSize), tickSize, "TickSize must be strictly positive.");

        return new SlippageConfiguration { PriceUnits = ticks * tickSize };
    }
}
