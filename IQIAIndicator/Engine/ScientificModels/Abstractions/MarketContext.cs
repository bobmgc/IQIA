namespace IQIAIndicator.Engine.ScientificModels.Abstractions;

/// <summary>
/// Scientific-layer market snapshot for a single evaluation run.
/// The contract carries the minimal scientific data plus optional market metadata.
/// </summary>
public sealed record MarketContext(
    DateTime Timestamp,
    decimal CurrentBar,
    IReadOnlyList<decimal> History,
    decimal CurrentPrice = 0m,
    string? Symbol = null,
    string? TimeFrame = null,
    string? Exchange = null,
    string? Session = null);
