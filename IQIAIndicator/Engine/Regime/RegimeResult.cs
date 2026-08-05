namespace IQIAIndicator.Engine.Regime;

/// <summary>
/// Résultat immutable du Statistical Evidence Engine.
/// Aucune référence à un indicateur technique — uniquement des métriques issues des modèles statistiques.
/// </summary>
public sealed class RegimeResult
{
    public required RegimeType            Regime            { get; init; }
    public required RegimeConfidence      Confidence        { get; init; }
    public required decimal               EvidenceScore     { get; init; }  // poids relatif du régime gagnant
    public required decimal               EvidenceAgreement { get; init; }  // fraction de modèles en accord
    public required int                   EvidenceCount     { get; init; }  // nombre de modèles prêts
    public required IReadOnlyList<string> WinningModels     { get; init; }  // noms des modèles qui ont voté gagnant
    public required DateTime              Timestamp         { get; init; }
}
