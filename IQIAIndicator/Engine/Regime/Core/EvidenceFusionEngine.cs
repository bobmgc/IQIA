namespace IQIAIndicator.Engine.Regime.Core;

/// <summary>
/// Moteur de fusion des évidences statistiques → RegimeResult.
///
/// Sprint 2.4 : logique de fusion à implémenter.
/// Actuellement : retourne Unknown sans aucun traitement.
///
/// Principe architectural :
///   Toute décision de régime appartient exclusivement à cette classe.
///   Les modèles scientifiques (ADF, KPSS, Hurst, …) ne décident rien.
///   Ils produisent des observations. Ce moteur interprète.
/// </summary>
public sealed class EvidenceFusionEngine
{
    public RegimeResult Fuse(EvidenceSet evidence) =>
        new()
        {
            Regime            = RegimeType.Unknown,
            Confidence        = new RegimeConfidence
            {
                Score           = 0m,
                NormalizedScore = 0m,
                Explanation     = "Fusion non implémentée — Sprint 2.4."
            },
            EvidenceScore     = 0m,
            EvidenceAgreement = 0m,
            EvidenceCount     = 0,
            WinningModels     = [],
            Timestamp         = evidence.Timestamp
        };
}
