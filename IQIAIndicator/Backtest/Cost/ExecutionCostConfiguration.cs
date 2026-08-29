namespace IQIAIndicator.Backtest.Cost;

/// <summary>
/// Sprint 15.25 (Lot 14.7, brief §9). Single, explicit configuration for the whole cost/slippage/execution
/// realism layer - the one object <see cref="PositionCostCalculator"/> needs alongside the existing
/// <see cref="Pnl.PnLConfiguration"/> (which already owns Instrument/Quantity - brief §10: never
/// duplicated here).
///
/// <see cref="Enabled"/> is a real kill switch (brief §9: "activation/désactivation des coûts"),
/// independent from whether the individual components happen to be zero: when false,
/// <see cref="PositionCostCalculator"/> skips cost logic entirely and every position's NetPnL equals its
/// GrossPnL, regardless of what the sub-configurations contain. <see cref="Disabled"/> is the recommended
/// default and is, by construction, EXACTLY the Lot 14.6 behaviour (brief §9: "configuration par défaut =
/// comportement Lot 14.6") - verified by the zero-cost-equivalence tests.
/// </summary>
public sealed record ExecutionCostConfiguration
{
    public required bool Enabled { get; init; }

    public required SlippageConfiguration Slippage { get; init; }

    public required SpreadConfiguration Spread { get; init; }

    public required CommissionConfiguration Commission { get; init; }

    public required FeesConfiguration Fees { get; init; }

    /// <summary>Brief §9's mandatory default: disabled, every component zero - reproduces Lot 14.6 exactly.</summary>
    public static ExecutionCostConfiguration Disabled() => new()
    {
        Enabled = false,
        Slippage = SlippageConfiguration.None(),
        Spread = SpreadConfiguration.None(),
        Commission = CommissionConfiguration.None(),
        Fees = FeesConfiguration.None()
    };

    /// <summary>Any omitted component defaults to its own zero/None value (brief §9's default list).</summary>
    public static ExecutionCostConfiguration Create(
        bool enabled,
        SlippageConfiguration? slippage = null,
        SpreadConfiguration? spread = null,
        CommissionConfiguration? commission = null,
        FeesConfiguration? fees = null) => new()
    {
        Enabled = enabled,
        Slippage = slippage ?? SlippageConfiguration.None(),
        Spread = spread ?? SpreadConfiguration.None(),
        Commission = commission ?? CommissionConfiguration.None(),
        Fees = fees ?? FeesConfiguration.None()
    };
}
