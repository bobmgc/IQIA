using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Regime.Core;

namespace IQIAIndicator.Engine.Decision.Core;

/// <summary>
/// Entrées immuables du moteur de décision, sans donnée de marché directe.
/// </summary>
public sealed record DecisionContext
{
    public required FusionResult FusionResult { get; init; }

    public required EvidenceSet Evidence { get; init; }
}