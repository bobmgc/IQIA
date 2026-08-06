using IQIAIndicator.Engine.Decision.Arbitration;
using IQIAIndicator.Engine.Decision.Rules;

namespace IQIAIndicator.Engine.Decision.Core;

/// <summary>
/// Orchestrateur des règles d'interprétation du marché, sans action de trading.
/// </summary>
public sealed class DecisionEngine
{
    public DecisionEngine(IReadOnlyList<IDecisionRule> rules)
        : this(rules, new DecisionArbitrator())
    {
    }

    public DecisionEngine(IReadOnlyList<IDecisionRule> rules, DecisionArbitrator arbitrator)
    {
        Rules = rules;
        Arbitrator = arbitrator;
    }

    public IReadOnlyList<IDecisionRule> Rules { get; }

    public DecisionArbitrator Arbitrator { get; }

    public DecisionResult Evaluate(DecisionContext context)
    {
        var candidates = new List<DecisionCandidate>();
        foreach (IDecisionRule rule in Rules)
        {
            var builder = new DecisionResultBuilder();
            rule.Evaluate(context, builder);

            if (builder.Candidate is not null)
                candidates.Add(builder.Candidate);
        }

        return Arbitrator.Arbitrate(candidates);
    }
}
