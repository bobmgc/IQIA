using IQIAIndicator.Core;

namespace IQIAIndicator.Engine.Regime.Evidence;

/// <summary>Contrat commun à chaque modèle statistique du Regime Engine.</summary>
public interface IRegimeEvidence
{
    string ModelName { get; }

    /// <summary>Met à jour l'état interne et retourne le résultat pour ce bar.</summary>
    EvidenceResult Compute(MarketContext context);
}
