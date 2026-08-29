using System;

namespace IQIAIndicator.Backtest.Risk;

/// <summary>
/// Sprint 15.25 (Lot 14.8, brief §8). Explicit, caller-supplied stop/risk distance, in PRICE UNITS - the
/// one input <see cref="Engine.Risk.RiskEngine"/> needs to size a trade that the backtest signal pipeline
/// cannot itself provide today (audited fact: <c>TradePlan.StopLoss</c> is always null in
/// <see cref="BacktestEngine.RunSignalPipeline"/>, since no <c>TradeRiskParameters</c> is ever passed - see
/// this lot's report §2). Never invented from an ATR, a fixed tick guess, or any other heuristic (brief
/// §8: "Ne PAS inventer un stop si la stratégie n'en fournit pas") - the three ways to build one below are
/// just three ways to ARRIVE at the same single resolved <see cref="PriceUnits"/> value, exactly mirroring
/// <c>Backtest.Cost.SlippageConfiguration</c>'s own construction pattern.
///
/// <see cref="PriceUnits"/> &lt;= 0 (including the default <see cref="None"/>) is the explicit "no risk
/// distance available" state - <see cref="PositionRiskEvaluator"/> treats it as
/// <see cref="PositionRiskReason.InvalidRiskDistance"/>, never as a zero-risk trade to size anyway (brief
/// §8: "retourner un résultat explicite indiquant que le sizing basé sur le risque n'est pas calculable").
/// </summary>
public sealed record RiskDistanceConfiguration
{
    public required decimal PriceUnits { get; init; }

    /// <summary>No risk distance configured - risk-based sizing is not calculable (brief §8).</summary>
    public static RiskDistanceConfiguration None() => new() { PriceUnits = 0m };

    public static RiskDistanceConfiguration Fixed(decimal priceUnits)
    {
        if (priceUnits < 0m)
            throw new ArgumentOutOfRangeException(nameof(priceUnits), priceUnits, "Risk distance cannot be negative.");

        return new RiskDistanceConfiguration { PriceUnits = priceUnits };
    }

    /// <summary>Same tick-conversion convention as <c>Backtest.Cost.SlippageConfiguration.FromTicks</c>.</summary>
    public static RiskDistanceConfiguration FromTicks(decimal ticks, decimal tickSize)
    {
        if (ticks < 0m)
            throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "Risk distance ticks cannot be negative.");

        if (tickSize <= 0m)
            throw new ArgumentOutOfRangeException(nameof(tickSize), tickSize, "TickSize must be strictly positive.");

        return new RiskDistanceConfiguration { PriceUnits = ticks * tickSize };
    }
}
