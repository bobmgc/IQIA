using IQIAIndicator.Engine.Decision.States;

namespace IQIAIndicator.Engine.Decision.Arbitration;

/// <summary>
/// Immutable hypothesis produced by a decision rule before arbitration.
/// </summary>
public sealed record DecisionCandidate
{
    public required MarketState MarketState { get; init; }

    public required double ScientificScore { get; init; }

    public required double QualityScore { get; init; }

    public required double FinalScore { get; init; }

    public required string Explanation { get; init; }

    public IReadOnlyList<string> TriggeredRules { get; init; } = [];

    public IReadOnlyList<string> RejectedRules { get; init; } = [];

    public string RuleExplanation { get; init; } = string.Empty;
}
