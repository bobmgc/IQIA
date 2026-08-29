using IQIAIndicator.Engine.Decision.Core;

namespace IQIAIndicator.Engine.Decision.Rules;

/// <summary>
/// Contrat des futures règles d'interprétation, sans action de trading.
/// </summary>
public interface IDecisionRule
{
    void Evaluate(DecisionContext context, DecisionResultBuilder builder);
}