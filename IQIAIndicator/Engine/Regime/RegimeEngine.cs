using IQIAIndicator.Core;
using IQIAIndicator.Engine.Regime.Evidence;

namespace IQIAIndicator.Engine.Regime;

/// <summary>
/// Orchestrateur du Statistical Evidence Engine.
/// Appelle chaque modele independant, passe les resultats au moteur de fusion.
/// Aucune regle metier ni heuristique ici — tout est dans les modeles et la fusion.
/// </summary>
public sealed class RegimeEngine
{
    private readonly IRegimeEvidence[] _models =
    [
        new AdfEvidence(),
        new KpssEvidence(),
        new HurstEvidence(),
        new HalfLifeEvidence(),
        new VarianceRatioEvidence(),
        new CusumEvidence(),
        new VolatilityEvidence()
    ];

    private readonly EvidenceFusionEngine _fusion = new();

    public RegimeResult Analyze(MarketContext context)
    {
        var results = new List<EvidenceResult>(_models.Length);
        foreach (var model in _models)
            results.Add(model.Compute(context));

        return _fusion.Fuse(results, context.Clock.CurrentTime);
    }
}
