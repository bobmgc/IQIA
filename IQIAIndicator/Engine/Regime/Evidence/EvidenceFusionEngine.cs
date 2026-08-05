using System.Linq;

namespace IQIAIndicator.Engine.Regime.Evidence;

/// <summary>
/// Agrège les preuves de tous les modèles par vote pondéré.
/// Ne connaît pas les détails internes des modèles — opère uniquement sur EvidenceResult.
/// Règle de fusion : chaque modèle vote pour un régime avec un poids = Score × Confidence.
/// Le régime ayant le plus de poids l'emporte.
/// </summary>
public sealed class EvidenceFusionEngine
{
    public RegimeResult Fuse(IReadOnlyList<EvidenceResult> results, DateTime timestamp)
    {
        var ready = results.Where(r => r.IsReady).ToList();

        if (ready.Count == 0)
            return BuildUnknown(timestamp);

        // Vote pondéré par régime suggéré
        var votes = new Dictionary<RegimeType, decimal>();
        foreach (var r in ready)
        {
            var weight = r.Score * r.Confidence;
            votes.TryAdd(r.RegimeHint, 0m);
            votes[r.RegimeHint] += weight;
        }

        // Régime gagnant
        var winner      = votes.MaxBy(kv => kv.Value);
        var totalWeight = ready.Sum(r => r.Score * r.Confidence);
        var evidenceScore = totalWeight > 0m ? winner.Value / totalWeight : 0m;

        var winningModels = ready
            .Where(r => r.RegimeHint == winner.Key)
            .Select(r => r.ModelName)
            .ToList();

        var agreement = (decimal)winningModels.Count / ready.Count;

        return new RegimeResult
        {
            Regime            = winner.Key,
            Confidence        = new RegimeConfidence
            {
                Score           = evidenceScore,
                NormalizedScore = evidenceScore,
                Explanation     = $"{winningModels.Count}/{ready.Count} modèles → {winner.Key}"
            },
            EvidenceScore     = evidenceScore,
            EvidenceAgreement = agreement,
            EvidenceCount     = ready.Count,
            WinningModels     = winningModels,
            Timestamp         = timestamp
        };
    }

    private static RegimeResult BuildUnknown(DateTime timestamp) => new()
    {
        Regime            = RegimeType.Unknown,
        Confidence        = new RegimeConfidence
        {
            Score = 0m, NormalizedScore = 0m,
            Explanation = "Période de chauffe — aucun modèle prêt."
        },
        EvidenceScore     = 0m,
        EvidenceAgreement = 0m,
        EvidenceCount     = 0,
        WinningModels     = [],
        Timestamp         = timestamp
    };
}
