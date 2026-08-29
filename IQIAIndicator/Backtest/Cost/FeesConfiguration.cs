using System;

namespace IQIAIndicator.Backtest.Cost;

/// <summary>
/// Sprint 15.25 (Lot 14.7, brief §7). A fee component strictly INDEPENDENT of
/// <see cref="CommissionConfiguration"/> (brief §7: "Permettre une composante de frais indépendante de la
/// commission") - e.g. exchange/regulatory fees billed alongside, not instead of, broker commission.
/// Applied per order (entry + exit), exactly like <see cref="CommissionConfiguration.PerOrder"/> - see
/// <see cref="PositionCostCalculator"/> for the exact formula.
/// </summary>
public sealed record FeesConfiguration
{
    public required decimal PerOrder { get; init; }

    /// <summary>Brief §12 "zero fees".</summary>
    public static FeesConfiguration None() => new() { PerOrder = 0m };

    /// <summary>Brief §12 "fixed fees".</summary>
    public static FeesConfiguration Create(decimal perOrder = 0m)
    {
        if (perOrder < 0m)
            throw new ArgumentOutOfRangeException(nameof(perOrder), perOrder, "Fees per order cannot be negative.");

        return new FeesConfiguration { PerOrder = perOrder };
    }
}
