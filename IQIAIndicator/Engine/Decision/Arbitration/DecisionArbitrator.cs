using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.States;

namespace IQIAIndicator.Engine.Decision.Arbitration;

/// <summary>
/// Pure arbitration layer over already computed decision candidates.
/// </summary>
public sealed class DecisionArbitrator
{
    public DecisionResult Arbitrate(IReadOnlyList<DecisionCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return new DecisionResult
            {
                Winner = MarketState.Unknown,
                WinnerScore = 0.0,
                Candidates = [],
                AmbiguityScore = 0.0,
                Explanation = "No Decision",
                State = MarketState.Unknown,
                Confidence = 0.0
            };
        }

        ImmutableArray<DecisionCandidate> orderedCandidates = candidates
            .OrderByDescending(candidate => candidate.FinalScore)
            .ToImmutableArray();
        DecisionCandidate winner = orderedCandidates[0];
        DecisionCandidate? runnerUp = orderedCandidates.Length > 1 ? orderedCandidates[1] : null;
        double runnerUpScore = runnerUp?.FinalScore ?? 0.0;
        double difference = winner.FinalScore - runnerUpScore;
        double ambiguityScore = Math.Clamp(1.0 - difference, 0.0, 1.0);

        return new DecisionResult
        {
            Winner = winner.MarketState,
            WinnerScore = winner.FinalScore,
            Candidates = orderedCandidates,
            AmbiguityScore = ambiguityScore,
            Explanation = BuildExplanation(winner, runnerUp, difference, ambiguityScore),
            State = winner.MarketState,
            Confidence = winner.FinalScore
        };
    }

    private static string BuildExplanation(
        DecisionCandidate winner,
        DecisionCandidate? runnerUp,
        double difference,
        double ambiguityScore) =>
        $"Winner : {winner.MarketState}{Environment.NewLine}" +
        $"Winner Score : {FormatScore(winner.FinalScore)}{Environment.NewLine}" +
        $"Runner Up : {FormatRunnerUp(runnerUp)}{Environment.NewLine}" +
        $"Difference : {FormatScore(difference)}{Environment.NewLine}" +
        $"Ambiguity : {FormatScore(ambiguityScore)}";

    private static string FormatRunnerUp(DecisionCandidate? runnerUp) =>
        runnerUp is null ? "None" : $"{runnerUp.MarketState} ({FormatScore(runnerUp.FinalScore)})";

    private static string FormatScore(double value) => value.ToString("F3", CultureInfo.InvariantCulture);
}
