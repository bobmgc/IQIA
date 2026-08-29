using System;

namespace IQIAIndicator.Backtest.Cost;

/// <summary>
/// Sprint 15.25 (Lot 14.7, brief §7). Commission charged on EACH order (an entry fill and an exit fill are
/// two separate orders - brief §7's worked example: "$2.00 par entrée, $2.00 par sortie"). Two independent,
/// additive components:
/// <list type="bullet">
/// <item><see cref="PerOrder"/> - a flat fee per order, independent of size.</item>
/// <item><see cref="PerUnit"/> - charged per contract/unit, e.g. "$0.50 par contrat" (brief §7).</item>
/// </list>
/// <see cref="PositionCostCalculator"/> applies both components to BOTH the entry and the exit order (a
/// standard round-trip commission), never just once - see its own doc comment for the exact formula.
/// </summary>
public sealed record CommissionConfiguration
{
    public required decimal PerOrder { get; init; }

    public required decimal PerUnit { get; init; }

    /// <summary>Brief §12 "zero commission".</summary>
    public static CommissionConfiguration None() => new() { PerOrder = 0m, PerUnit = 0m };

    public static CommissionConfiguration Create(decimal perOrder = 0m, decimal perUnit = 0m)
    {
        if (perOrder < 0m)
            throw new ArgumentOutOfRangeException(nameof(perOrder), perOrder, "Commission per order cannot be negative.");

        if (perUnit < 0m)
            throw new ArgumentOutOfRangeException(nameof(perUnit), perUnit, "Commission per unit cannot be negative.");

        return new CommissionConfiguration { PerOrder = perOrder, PerUnit = perUnit };
    }
}
