namespace IQIAIndicator.Engine.Regime;

/// <summary>Niveau de confiance associé au régime détecté.</summary>
public sealed class RegimeConfidence
{
    public required decimal Score           { get; init; }  // score brut du signal dominant
    public required decimal NormalizedScore { get; init; }  // 0..1, utilisé pour l'affichage futur
    public required string  Explanation     { get; init; }  // description lisible pour le Display Engine
}
