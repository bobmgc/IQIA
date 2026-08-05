namespace IQIAIndicator.Core;

/// <summary>
/// Conteneur immutable de toutes les données de marché pour un bar donné.
/// Source de vérité unique transmise aux moteurs aval (Régime, Décision, Risque).
/// Aucune logique métier ne réside dans cette classe.
/// </summary>
public sealed class MarketContext
{
    public required int              BarIndex   { get; init; }
    public required string           TimeFrame  { get; init; }

    public required PriceInfo        Price      { get; init; }
    public required VolumeInfo       Volume     { get; init; }
    public required InstrumentInfo   Instrument { get; init; }
    public required MarketClock      Clock      { get; init; }
    public required ExecutionContext Execution  { get; init; }
}
