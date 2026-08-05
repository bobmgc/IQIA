namespace IQIAIndicator.Engine.Regime.Evidence;

/// <summary>
/// Résultat produit par un modèle statistique indépendant.
/// Le moteur de fusion utilise uniquement ces champs — il ne connaît pas l'intérieur des modèles.
/// </summary>
public sealed class EvidenceResult
{
    public required string     ModelName   { get; init; }
    public required decimal    Score       { get; init; }      // force du signal [0..1]
    public required decimal    Confidence  { get; init; }      // fiabilité de l'estimation [0..1]
    public required int        Direction   { get; init; }      // +1 tendance · -1 stationnarité · 0 neutre
    public required string     Explanation { get; init; }
    public required bool       IsReady     { get; init; }      // false pendant le warmup
    public required RegimeType RegimeHint  { get; init; }      // régime suggéré par ce modèle seul
}
