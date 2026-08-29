namespace IQIAIndicator.Backtest.Risk;

/// <summary>
/// Sprint 15.25 (Lot 14.8, brief §9). Explicit reason a <see cref="RiskEvaluationResult"/> did or did not
/// allow a position - never an opaque boolean (brief §9: "Éviter les booléens opaques sans raison"). A
/// NEW, backtest-scoped enum rather than an extension of <c>Engine.Risk.RiskRejectionReason</c> (brief
/// §1.1/§26: that file lives outside <c>Backtest/</c>, feeds the live ATAS path too, and this lot has no
/// justified additive need to touch it) - <see cref="PositionRiskEvaluator"/> maps
/// <c>Engine.Risk.RiskAssessment.RejectionReasons</c> onto this smaller, backtest-facing set (see its own
/// mapping table) and adds <see cref="ExposureLimitExceeded"/>, the one genuinely new concept this lot
/// introduces (brief §10 - confirmed by repo-wide search to not exist anywhere before this lot).
/// </summary>
public enum PositionRiskReason
{
    /// <summary>The position was allowed - AllowedQuantity &gt; 0.</summary>
    Allowed,

    /// <summary>The underlying <see cref="Execution.SimulatedPosition"/> never reached
    /// <see cref="Execution.PositionStatus.Closed"/> - there is nothing to risk-evaluate.</summary>
    PositionNotExecutable,

    /// <summary>Reserved for a structurally invalid <see cref="BacktestRiskConfiguration"/> - unreachable
    /// today since <see cref="BacktestRiskConfiguration.Create"/>/<see cref="RiskDistanceConfiguration"/>
    /// validate eagerly at construction (mirrors <c>Engine.Risk.RiskRejectionReason.UNKNOWN_ERROR</c>'s own
    /// "declared, currently unreachable" precedent).</summary>
    InvalidConfiguration,

    /// <summary>Maps from <c>Engine.Risk.RiskRejectionReason.INSTRUMENT_SPEC_INVALID</c>.</summary>
    InvalidInstrument,

    /// <summary>No usable stop/risk distance was configured (brief §8) - checked before ever calling
    /// <c>Engine.Risk.RiskEngine.Evaluate</c>. Also covers the engine's own
    /// <c>RiskRejectionReason.INVALID_STOP_LOSS</c>.</summary>
    InvalidRiskDistance,

    /// <summary>Maps from <c>RiskRejectionReason.INVALID_CAPITAL</c>, <c>INVALID_EQUITY</c>,
    /// <c>RISK_BUDGET_EXCEEDED</c>, <c>MAX_DRAWDOWN_REACHED</c>, <c>DAILY_LOSS_LIMIT</c>,
    /// <c>OPEN_RISK_LIMIT</c> - every reason the account/policy could not afford this trade's risk.</summary>
    RiskLimitExceeded,

    /// <summary>Maps from <c>RiskRejectionReason.QUANTITY_LIMIT</c> - the affordable size sits below the
    /// instrument's MinQuantity.</summary>
    MaxQuantityExceeded,

    /// <summary>Brief §10 - the only reason this lot's own exposure check produces (never
    /// <c>Engine.Risk.RiskEngine</c>, which has no exposure concept).</summary>
    ExposureLimitExceeded,

    /// <summary>Maps from <c>RiskRejectionReason.POSITION_SIZE_INVALID</c> - the risk budget rounds down to
    /// zero whole contracts.</summary>
    ZeroRisk
}
