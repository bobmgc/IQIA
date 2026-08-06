using System.Globalization;
using System.Linq;
using IQIAIndicator.Engine.Decision.Arbitration;
using IQIAIndicator.Engine.Decision.Rules;
using IQIAIndicator.Engine.Decision.States;

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

            if (TryCreateCandidate(builder.Build(), out DecisionCandidate? candidate) && candidate is not null)
                candidates.Add(candidate);
        }

        return Arbitrator.Arbitrate(candidates);
    }

    private static bool TryCreateCandidate(DecisionResult ruleResult, out DecisionCandidate? candidate)
    {
        candidate = null;

        if (ruleResult.State == MarketState.Unknown || ruleResult.TriggeredRules.Count == 0)
            return false;

        if (!TryReadScore(ruleResult.Explanation, "Scientific Score : ", out double scientificScore) ||
            !TryReadScore(ruleResult.Explanation, "Quality Score : ", out double qualityScore))
        {
            return false;
        }

        candidate = new DecisionCandidate
        {
            MarketState = ruleResult.State,
            ScientificScore = scientificScore,
            QualityScore = qualityScore,
            FinalScore = ruleResult.Confidence,
            Explanation = ruleResult.Explanation
        };

        return true;
    }

    private static bool TryReadScore(string explanation, string prefix, out double score)
    {
        score = 0.0;
        string? line = explanation
            .Split(Environment.NewLine, StringSplitOptions.None)
            .FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));

        return line is not null &&
            double.TryParse(
                line[prefix.Length..],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out score);
    }
}
