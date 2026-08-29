using System;

namespace IQIAIndicator.Backtest.Risk;

/// <summary>
/// Sprint 15.25 (Lot 14.8, brief §4). Single, explicit configuration for the backtest-side risk/position-
/// sizing layer. Deliberately does NOT duplicate <c>InitialCapital</c>, <c>RiskPerTrade</c> or
/// <c>MaxPositionQuantity</c> as new fields (brief §2: "Ne pas créer de doublons") - those already exist
/// and are reused as-is: <c>InitialCapital</c> is <see cref="BacktestScenario.InitialCapital"/>,
/// <c>RiskPerTrade</c> is <c>Engine.Risk.RiskPolicy.MaxRiskPerTradePercent</c>/<c>MaxRiskPerTradeAmount</c>,
/// <c>MaxPositionQuantity</c> is <c>RiskPolicy.MaxPositionSize</c> and
/// <c>Engine.Risk.InstrumentRiskSpecification.MaxQuantity</c> - both already carried by
/// <see cref="BacktestScenario"/> and already enforced by <c>Engine.Risk.RiskEngine.Evaluate</c>. This type
/// only adds what genuinely does not exist anywhere else in the codebase: the stop/risk distance input
/// (<see cref="RiskDistance"/>, brief §8) and the exposure limit (<see cref="MaxExposure"/>, brief §10 -
/// confirmed by repo-wide search to have zero prior implementation).
///
/// <see cref="EnableRiskControls"/> is a real kill switch (brief §4), independent of the sub-values: when
/// false, <see cref="PositionRiskEvaluator"/> skips risk/exposure logic entirely and every position's
/// AllowedQuantity equals its RequestedQuantity - reproducing Lot 14.6/14.7's historical behaviour exactly
/// (brief §4: "le comportement doit rester compatible avec le comportement historique").
/// </summary>
public sealed record BacktestRiskConfiguration
{
    public required bool EnableRiskControls { get; init; }

    public required RiskDistanceConfiguration RiskDistance { get; init; }

    /// <summary>Maximum notional exposure (ExecutedEntryPrice x Quantity x ContractMultiplier), in account
    /// currency. Null means unlimited (brief §4 default).</summary>
    public decimal? MaxExposure { get; init; }

    /// <summary>Brief §4's mandatory default: risk controls disabled, no risk distance, unlimited exposure
    /// - reproduces Lot 14.6/14.7 behaviour exactly.</summary>
    public static BacktestRiskConfiguration Disabled() => new()
    {
        EnableRiskControls = false,
        RiskDistance = RiskDistanceConfiguration.None(),
        MaxExposure = null
    };

    public static BacktestRiskConfiguration Create(
        bool enableRiskControls,
        RiskDistanceConfiguration? riskDistance = null,
        decimal? maxExposure = null)
    {
        if (maxExposure is decimal me && me <= 0m)
            throw new ArgumentOutOfRangeException(nameof(maxExposure), me, "MaxExposure, when supplied, must be strictly positive.");

        return new BacktestRiskConfiguration
        {
            EnableRiskControls = enableRiskControls,
            RiskDistance = riskDistance ?? RiskDistanceConfiguration.None(),
            MaxExposure = maxExposure
        };
    }
}
