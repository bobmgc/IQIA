namespace IQIAIndicator.Engine.Risk;

/// <summary>
/// Sprint 15.25 (Lot 10, Section 10). Extension point for a future scientifically-validated stop-loss
/// methodology (ATR-based, structure-based, volatility-based - see QDE-012_StopLoss_Calibration_Protocol.md
/// for the candidate formulas already researched but not yet approved for production). No production
/// formula is implemented here - RiskEngineRequest.StopLoss is always caller-provided today via
/// ProvidedStopLossStrategy, the only implementation in this sprint.
/// </summary>
public interface IStopLossStrategy
{
    /// <summary>Resolves a stop-loss price for the given direction/entry, or null if this strategy cannot produce one.</summary>
    decimal? Resolve(TradeDirection direction, decimal entryPrice);
}

/// <summary>Pass-through strategy: returns exactly the stop-loss the caller already supplied. Never derives one.</summary>
public sealed class ProvidedStopLossStrategy(decimal? stopLoss) : IStopLossStrategy
{
    public decimal? Resolve(TradeDirection direction, decimal entryPrice) => stopLoss;
}
