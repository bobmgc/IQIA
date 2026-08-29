using System;
using System.Collections.Generic;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.EntryTrigger;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Execution;

/// <summary>
/// Sprint 15.25 (Lot 15.4, brief §22/§23). Determinism and run isolation for the NEW intrabar Stop/Target
/// logic specifically (StopLoss/TakeProfit/Ambiguous exits) - complements the pre-existing TimeHorizon-only
/// coverage in <see cref="ExecutionSimulatorFormulaTests"/> and <see cref="ExecutionSimulatorIntegrationTests"/>.
/// <see cref="ExecutionSimulator"/> is a static class with no fields (stateless by construction, see its own
/// doc comment) - these tests confirm that holds empirically for the code paths this lot added, rather than
/// only by inspection.
/// </summary>
public sealed class ExecutionIntrabarDeterminismTests
{
    private static readonly DateTime Anchor = new(2026, 1, 5, 14, 30, 0, DateTimeKind.Utc);

    private static HistoricalBar Bar(int minutesFromAnchor, decimal open, decimal high, decimal low, decimal close) =>
        new(Anchor.AddMinutes(minutesFromAnchor), open, high, low, close, Volume: 100m);

    private static HistoricalBar Flat(int minutesFromAnchor, decimal price) =>
        Bar(minutesFromAnchor, price, price, price, price);

    private static ExecutionCandidate Candidate(
        int signalBarIndex, DirectionCandidate direction, decimal? entryPrice,
        decimal? stopLoss = null, decimal? takeProfit = null) =>
        new(signalBarIndex, Anchor.AddMinutes(signalBarIndex * 5), direction, entryPrice, null, stopLoss, takeProfit);

    // A fixture that produces a StopLoss exit, one that produces a TakeProfit exit, and one that produces
    // an Ambiguous exit - all via the NEW intrabar walk.
    private static List<HistoricalBar> StopLossFixture() => new()
    {
        Flat(0, 999m),
        Flat(5, 100m),
        Bar(10, 100m, 101m, 94m, 96m),
        Bar(15, 100m, 101m, 99m, 100m),
    };

    private static List<HistoricalBar> TakeProfitFixture() => new()
    {
        Flat(0, 999m),
        Flat(5, 100m),
        Bar(10, 100m, 106m, 99m, 104m),
        Bar(15, 100m, 101m, 99m, 100m),
    };

    private static List<HistoricalBar> AmbiguousFixture() => new()
    {
        Flat(0, 999m),
        Flat(5, 100m),
        Bar(10, 100m, 106m, 94m, 100m),
        Bar(15, 100m, 101m, 99m, 100m),
    };

    private static ExecutionCandidate StandardCandidate() =>
        Candidate(0, DirectionCandidate.BUY_CANDIDATE, 100m, stopLoss: 95m, takeProfit: 105m);

    // Horizon=1: entryBarIndex=1, exitBarIndex=2 - exactly where each fixture above places its touch.
    private static ExecutionConfiguration StandardConfig() => ExecutionConfiguration.Create(1);

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // §22: determinism - same TradePlan + bars + config -> bit-identical SimulatedPosition.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("StopLoss")]
    [InlineData("TakeProfit")]
    [InlineData("Ambiguous")]
    public void SameInputs_RunTwice_ProduceBitIdenticalSimulatedPositions(string fixtureName)
    {
        List<HistoricalBar> bars = fixtureName switch
        {
            "StopLoss" => StopLossFixture(),
            "TakeProfit" => TakeProfitFixture(),
            "Ambiguous" => AmbiguousFixture(),
            _ => throw new ArgumentOutOfRangeException(nameof(fixtureName))
        };
        ExecutionCandidate candidate = StandardCandidate();
        ExecutionConfiguration config = StandardConfig();

        SimulatedPosition instanceA = ExecutionSimulator.SimulateCore(bars, candidate, config);
        SimulatedPosition instanceB = ExecutionSimulator.SimulateCore(bars, candidate, config);

        Assert.Equal(instanceA, instanceB); // record equality: every field
        Assert.Equal(instanceA.ExitReason, instanceB.ExitReason);
        Assert.Equal(instanceA.ExitPrice, instanceB.ExitPrice);
        Assert.Equal(instanceA.ExitBarIndex, instanceB.ExitBarIndex);
    }

    [Fact]
    public void SameInputs_RunTwice_WithARealWallClockDelayBetweenRuns_StillProducesTheSameResult()
    {
        List<HistoricalBar> bars = StopLossFixture();
        ExecutionCandidate candidate = StandardCandidate();
        ExecutionConfiguration config = StandardConfig();

        SimulatedPosition first = ExecutionSimulator.SimulateCore(bars, candidate, config);
        System.Threading.Thread.Sleep(50);
        SimulatedPosition second = ExecutionSimulator.SimulateCore(bars, candidate, config);

        Assert.Equal(first, second);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // §23: run isolation - RunA -> RunB (different scenario) -> RunA, repeated RunA must be identical.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RunA_RunB_RunA_Interleaved_ProducesIdenticalResultsForTheRepeatedRunA()
    {
        List<HistoricalBar> barsA = StopLossFixture();
        ExecutionCandidate candidateA = StandardCandidate();
        ExecutionConfiguration configA = StandardConfig();

        List<HistoricalBar> barsB = TakeProfitFixture();
        ExecutionCandidate candidateB = Candidate(0, DirectionCandidate.SELL_CANDIDATE, 100m, stopLoss: 105m, takeProfit: 95m);
        ExecutionConfiguration configB = ExecutionConfiguration.Create(7);

        SimulatedPosition runA1 = ExecutionSimulator.SimulateCore(barsA, candidateA, configA);
        SimulatedPosition runBIntervening = ExecutionSimulator.SimulateCore(barsB, candidateB, configB);
        SimulatedPosition runA2 = ExecutionSimulator.SimulateCore(barsA, candidateA, configA);

        Assert.Equal(runA1, runA2);
        Assert.Equal(ExitReason.StopLoss, runA1.ExitReason);
        // runBIntervening exercised a completely different (SELL, TakeProfit-fixture-labelled-bars-but-used-
        // as-a-different-scenario) run in between - its outcome is irrelevant to this test, only that its
        // execution never perturbed the static ExecutionSimulator's (nonexistent) state.
        _ = runBIntervening;
    }

    [Fact]
    public void TwoIndependentCallSites_OnTheSameFixture_ProduceIdenticalResults()
    {
        // ExecutionSimulator is a static class - "two instances" doesn't apply the way it would for an
        // instantiable engine, so this test instead confirms two independently-constructed candidate/bar
        // inputs (never sharing a reference) produce identical output, which is the isolation property
        // that actually matters for a stateless static class.
        SimulatedPosition first = ExecutionSimulator.SimulateCore(AmbiguousFixture(), StandardCandidate(), StandardConfig());
        SimulatedPosition second = ExecutionSimulator.SimulateCore(AmbiguousFixture(), StandardCandidate(), StandardConfig());

        Assert.Equal(first, second);
        Assert.Equal(ExitReason.Ambiguous, first.ExitReason);
    }

    [Fact]
    public void ManyInterleavedRunsOfDifferentExitReasons_NeverCrossContaminate()
    {
        // Hammer the static class with alternating StopLoss/TakeProfit/Ambiguous scenarios and confirm
        // every single one keeps returning its own correct, stable result - no shared mutable state to leak.
        for (int iteration = 0; iteration < 25; iteration++)
        {
            SimulatedPosition stop = ExecutionSimulator.SimulateCore(StopLossFixture(), StandardCandidate(), StandardConfig());
            SimulatedPosition target = ExecutionSimulator.SimulateCore(TakeProfitFixture(), StandardCandidate(), StandardConfig());
            SimulatedPosition ambiguous = ExecutionSimulator.SimulateCore(AmbiguousFixture(), StandardCandidate(), StandardConfig());

            Assert.Equal(ExitReason.StopLoss, stop.ExitReason);
            Assert.Equal(ExitReason.TakeProfit, target.ExitReason);
            Assert.Equal(ExitReason.Ambiguous, ambiguous.ExitReason);
        }
    }
}
