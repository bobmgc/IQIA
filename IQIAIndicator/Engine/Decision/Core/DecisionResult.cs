using System.Collections.Immutable;
using IQIAIndicator.Engine.Decision.Arbitration;
using IQIAIndicator.Engine.Decision.States;

namespace IQIAIndicator.Engine.Decision.Core;

/// <summary>
/// Interprétation immuable produite par les règles de décision.
/// </summary>
public sealed record DecisionResult
{
    public MarketState Winner { get; init; } = MarketState.Unknown;

    public double WinnerScore { get; init; }

    public ImmutableArray<DecisionCandidate> Candidates { get; init; } = [];

    public double AmbiguityScore { get; init; }

    public MarketState State { get; init; }

    public double Confidence { get; init; }

    public string Explanation { get; init; } = string.Empty;

    public IReadOnlyList<string> TriggeredRules { get; init; } = [];

    public IReadOnlyList<string> RejectedRules { get; init; } = [];
}

/// <summary>
/// Point unique de construction d'un résultat de décision immuable.
/// </summary>
public sealed class DecisionResultBuilder
{
    public MarketState State { get; set; } = MarketState.Unknown;

    public double Confidence { get; set; }

    public string Explanation { get; set; } = "No Decision Rule";

    public List<string> TriggeredRules { get; } = [];

    public List<string> RejectedRules { get; } = [];

    public DecisionResult Build() => new()
    {
        Winner = State,
        WinnerScore = Confidence,
        Candidates = [],
        AmbiguityScore = 0.0,
        State = State,
        Confidence = Confidence,
        Explanation = Explanation,
        TriggeredRules = TriggeredRules.AsReadOnly(),
        RejectedRules = RejectedRules.AsReadOnly()
    };
}
