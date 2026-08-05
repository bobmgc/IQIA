namespace IQIAIndicator.Core;

/// <summary>
/// Identification de la session de marché (nom, horaires d'ouverture/clôture).
/// Les données temporelles et positionnelles du bar sont dans MarketClock.
/// </summary>
public readonly record struct SessionInfo(
    string   Name,
    DateTime MarketOpen,
    DateTime MarketClose
);
