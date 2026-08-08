namespace IQIAIndicator.Engine.ScientificModels.Abstractions;

/// <summary>
/// Scientific-layer market snapshot for a single evaluation run.
/// The contract remains intentionally minimal and datatype-safe.
/// </summary>
public sealed record MarketContext(
    DateTime Timestamp,
    decimal CurrentBar,
    IReadOnlyList<decimal> History);
