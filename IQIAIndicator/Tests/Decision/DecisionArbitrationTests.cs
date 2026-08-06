using IQIAIndicator.Engine.Decision.Arbitration;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Decision.Rules;
using IQIAIndicator.Engine.Decision.States;

namespace IQIAIndicator.Tests.Decision;

/// <summary>
/// Contract checks for pure decision candidate arbitration.
/// </summary>
public static class DecisionArbitrationTests
{
    public static void RunAll()
    {
        AssertClearWinner();
        AssertCloseCandidates();
        AssertWeakScoresKeepBestCandidate();
        AssertNoCandidates();
        AssertDecisionEngineProducesStructuredCandidates();
    }

    private static void AssertClearWinner()
    {
        DecisionResult result = Arbitrate(
            Candidate(MarketState.StableRange, 0.90),
            Candidate(MarketState.Trending, 0.70),
            Candidate(MarketState.MeanReverting, 0.40),
            Candidate(MarketState.StructuralBreak, 0.20),
            Candidate(MarketState.RandomWalk, 0.10));

        Assert(result.Winner == MarketState.StableRange, "The highest final score must win.");
        Assert(result.State == MarketState.StableRange, "State must mirror the winner for compatibility.");
        AssertClose(0.90, result.WinnerScore, "Winner score must be preserved.");
        AssertClose(0.90, result.Confidence, "Confidence must mirror winner score for compatibility.");
        AssertClose(0.80, result.AmbiguityScore, "A clear winner must have lower ambiguity than close candidates.");
        Assert(result.Candidates.Length == 5, "All candidates must be preserved.");
        Assert(result.Candidates[0].MarketState == MarketState.StableRange, "Candidates must be sorted by final score.");
        Assert(result.Explanation.Contains("Winner : StableRange"), "Winner must be explained.");
        Assert(result.Explanation.Contains("Runner Up : Trending (0.700)"), "Runner up must be explained.");
        Assert(result.Explanation.Contains("Difference : 0.200"), "Difference must be explained.");
        Assert(result.Explanation.Contains("Ambiguity : 0.800"), "Ambiguity must be explained.");
    }

    private static void AssertCloseCandidates()
    {
        DecisionResult result = Arbitrate(
            Candidate(MarketState.StableRange, 0.81),
            Candidate(MarketState.MeanReverting, 0.80));

        Assert(result.Winner == MarketState.StableRange, "The highest close candidate must still win.");
        AssertClose(0.81, result.WinnerScore, "Winner score must be preserved.");
        AssertClose(0.99, result.AmbiguityScore, "Close candidates must produce high ambiguity.");
        Assert(result.Candidates.Length == 2, "Both candidates must be preserved.");
        Assert(result.Explanation.Contains("Runner Up : MeanReverting (0.800)"), "Close runner up must be explained.");
    }

    private static void AssertWeakScoresKeepBestCandidate()
    {
        DecisionResult result = Arbitrate(
            Candidate(MarketState.RandomWalk, 0.22),
            Candidate(MarketState.StructuralBreak, 0.20),
            Candidate(MarketState.Trending, 0.18));

        Assert(result.Winner == MarketState.RandomWalk, "Weak scores must still keep the best candidate.");
        AssertClose(0.22, result.WinnerScore, "Weak winner score must be preserved.");
        AssertClose(0.98, result.AmbiguityScore, "Close weak scores must still report ambiguity.");
        Assert(result.Candidates.Length == 3, "All weak candidates must be preserved.");
    }

    private static void AssertNoCandidates()
    {
        DecisionResult result = Arbitrate();

        Assert(result.Winner == MarketState.Unknown, "No candidates must return Unknown.");
        Assert(result.State == MarketState.Unknown, "State must remain Unknown when there is no decision.");
        Assert(result.WinnerScore == 0.0, "No candidates must produce a zero winner score.");
        Assert(result.Confidence == 0.0, "No candidates must produce zero compatibility confidence.");
        Assert(result.AmbiguityScore == 0.0, "No candidates must produce zero ambiguity.");
        Assert(result.Candidates.Length == 0, "No candidates must return an empty candidate collection.");
        Assert(result.Explanation == "No Decision", "No candidates must return the no decision explanation.");
    }

    private static void AssertDecisionEngineProducesStructuredCandidates()
    {
        var rules = new[]
        {
            new CandidateRule(MarketState.StableRange, 0.70, 0.80, 0.71),
            new CandidateRule(MarketState.Trending, 0.90, 0.90, 0.90),
            new CandidateRule(MarketState.MeanReverting, 0.40, 0.60, 0.42),
            new CandidateRule(MarketState.StructuralBreak, 0.20, 0.50, 0.23),
            new CandidateRule(MarketState.RandomWalk, 0.10, 0.70, 0.16)
        };
        var engine = new DecisionEngine(rules);

        DecisionResult result = engine.Evaluate(new DecisionContext
        {
            FusionResult = null!,
            Evidence = null!
        });

        Assert(rules.Length == 5, "The test engine must register five rules.");
        Assert(Array.TrueForAll(rules, rule => rule.ExecutionCount == 1), "Each rule must be executed exactly once.");
        Assert(result.Candidates.Length == 5, "The engine must produce five structured candidates.");
        Assert(result.Winner == MarketState.Trending, "The arbitrator must receive the candidates and select the best one.");
        AssertClose(0.90, result.WinnerScore, "Winner score must come from the structured candidate.");
        Assert(result.Candidates[0].Explanation == "human text without scores", "Explanation must remain display-only text.");
        AssertClose(0.90, result.Candidates[0].ScientificScore, "Scientific score must be structured data.");
        AssertClose(0.90, result.Candidates[0].QualityScore, "Quality score must be structured data.");
        AssertClose(0.90, result.Candidates[0].FinalScore, "Final score must be structured data.");
    }

    private static DecisionResult Arbitrate(params DecisionCandidate[] candidates) =>
        new DecisionArbitrator().Arbitrate(candidates);

    private static DecisionCandidate Candidate(MarketState marketState, double finalScore) => new()
    {
        State = marketState,
        ScientificScore = finalScore,
        QualityScore = finalScore,
        FinalScore = finalScore,
        Explanation = string.Empty
    };

    private static void AssertClose(double expected, double actual, string message)
    {
        if (Math.Abs(expected - actual) > 1e-12)
            throw new InvalidOperationException($"{message} Expected={expected:F3}; Actual={actual:F3}.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class CandidateRule : IDecisionRule
    {
        private readonly MarketState _state;
        private readonly double _scientificScore;
        private readonly double _qualityScore;
        private readonly double _finalScore;

        public CandidateRule(
            MarketState state,
            double scientificScore,
            double qualityScore,
            double finalScore)
        {
            _state = state;
            _scientificScore = scientificScore;
            _qualityScore = qualityScore;
            _finalScore = finalScore;
        }

        public int ExecutionCount { get; private set; }

        public void Evaluate(DecisionContext context, DecisionResultBuilder builder)
        {
            ExecutionCount++;
            const string explanation = "human text without scores";
            builder.SetCandidate(_state, _scientificScore, _qualityScore, _finalScore, explanation);
            builder.State = _state;
            builder.Confidence = _finalScore;
            builder.Explanation = explanation;
            builder.TriggeredRules.Add(nameof(CandidateRule));
        }
    }
}
