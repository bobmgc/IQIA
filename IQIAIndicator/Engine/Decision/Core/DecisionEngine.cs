using IQIAIndicator.Engine.Decision.Rules;

namespace IQIAIndicator.Engine.Decision.Core;

/// <summary>
/// Orchestrateur des règles d'interprétation du marché, sans action de trading.
/// </summary>
public sealed class DecisionEngine
{
    public DecisionEngine(IReadOnlyList<IDecisionRule> rules)
    {
        Rules = rules;
    }

    public IReadOnlyList<IDecisionRule> Rules { get; }

    public DecisionResult Evaluate(DecisionContext context)
    {
        var builder = new DecisionResultBuilder();
        foreach (IDecisionRule rule in Rules)
            rule.Evaluate(context, builder);

        return builder.Build();
    }
}