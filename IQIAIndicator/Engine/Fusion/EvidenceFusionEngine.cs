using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;

namespace IQIAIndicator.Engine.Fusion;

/// <summary>
/// Orchestrateur vide préparant le futur pipeline de fusion des évidences.
/// </summary>
public sealed class EvidenceFusionEngine
{
    public EvidenceFusionEngine(IReadOnlyList<IFusionRule> rules)
    {
        Rules = rules;
    }

    public IReadOnlyList<IFusionRule> Rules { get; }

    public FusionResult Fuse(FusionContext context)
    {
        var builder = new FusionResultBuilder();
        foreach (IFusionRule rule in Rules)
            rule.Evaluate(context, builder);

        return builder.Build();
    }
}