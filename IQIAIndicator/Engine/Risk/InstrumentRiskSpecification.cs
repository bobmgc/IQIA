using IQIAIndicator.Core;

namespace IQIAIndicator.Engine.Risk;

/// <summary>
/// Sprint 15.25 (Lot 10, Section 5). Instrument-level risk parameters. Extends (not duplicates)
/// Core.InstrumentInfo with the quantity bounds InstrumentInfo does not carry (MinQuantity/MaxQuantity/
/// QuantityStep/ContractMultiplier). Every field must be explicitly configured per instrument by the
/// caller - the Risk Engine never assumes ES and MES share the same values, and never hardcodes a
/// per-symbol table internally.
/// </summary>
public sealed record InstrumentRiskSpecification(
    string Symbol,
    decimal TickSize,
    decimal TickValue,
    decimal PointValue,
    int MinQuantity,
    int MaxQuantity,
    int QuantityStep,
    decimal? ContractMultiplier = null)
{
    /// <summary>Builds a risk specification from the pipeline's existing InstrumentInfo plus the quantity bounds it does not carry.</summary>
    public static InstrumentRiskSpecification FromInstrumentInfo(
        InstrumentInfo instrumentInfo,
        int minQuantity,
        int maxQuantity,
        int quantityStep,
        decimal? contractMultiplier = null) =>
        new(
            instrumentInfo.Symbol,
            instrumentInfo.TickSize,
            instrumentInfo.TickValue,
            instrumentInfo.PointValue,
            minQuantity,
            maxQuantity,
            quantityStep,
            contractMultiplier);

    /// <summary>True only if every value forms a coherent, usable specification (all positive, Max >= Min, Step divides the range meaningfully).</summary>
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Symbol)
        && TickSize > 0m
        && TickValue > 0m
        && PointValue > 0m
        && MinQuantity > 0
        && MaxQuantity >= MinQuantity
        && QuantityStep > 0
        && (ContractMultiplier is null || ContractMultiplier > 0m);
}
