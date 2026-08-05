namespace IQIAIndicator.Core;

/// <summary>Métadonnées statiques de l'instrument négocié (tick, point, décimales).</summary>
public readonly record struct InstrumentInfo(
    string  Symbol,
    decimal TickSize,
    decimal TickValue,
    decimal PointValue,
    int     Decimals
);
