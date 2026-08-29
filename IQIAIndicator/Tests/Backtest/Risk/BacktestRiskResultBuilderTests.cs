using System;
using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Backtest.Cost;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Pnl;
using IQIAIndicator.Backtest.Risk;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Risk;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Risk;

/// <summary>
/// Sprint 15.25 (Lot 14.8, brief §13/§15/§16/§17/§18/§20). <see cref="BacktestRiskResultBuilder"/> -
/// dynamic equity across sequential trades, rejection producing no side effects, zero-risk/zero-cost
/// equivalence to Lot 14.6/14.7, run isolation, and determinism.
/// </summary>
public sealed class BacktestRiskResultBuilderTests
{
    private static readonly DateTime Anchor = new(2026, 1, 5, 14, 30, 0, DateTimeKind.Utc);

    private static InstrumentRiskSpecification EsRisk() =>
        new("ES", TickSize: 0.25m, TickValue: 12.5m, PointValue: 50m, MinQuantity: 1, MaxQuantity: 1000, QuantityStep: 1);

    private static InstrumentPnLSpecification EsPnl() => InstrumentPnLSpecification.Create("ES", 50m, "USD");

    private static RiskPolicy Policy(decimal riskPerTradePercent) => new(riskPerTradePercent, null, null, null, null, null, null, null, null, null);

    private static SimulatedPosition Closed(int id, DateTime exitTs, DirectionCandidate direction, decimal entry, decimal exit) =>
        new(id, PositionStatus.Closed, null, direction, exitTs.AddMinutes(-5), entry, 0, exitTs, exit, 10, ExitReason.TimeHorizon, 10,
            direction == DirectionCandidate.BUY_CANDIDATE ? exit - entry : entry - exit, 0.0);

    // ── §13: dynamic equity - the brief's own worked example, exactly ──────────────────────────────

    [Fact]
    public void DynamicEquity_SecondTradesMaximumAllowedRisk_ReflectsTheFirstTradesOutcome()
    {
        // Trade 1: a $2000 loss (GrossPriceMove=-40 * PointValue=50 * Quantity=1) -> Equity 100000 -> 98000.
        // Trade 2 should then see MaximumAllowedRisk = 98000 * 1% = 980 (brief §13's own numbers).
        var positions = new List<SimulatedPosition>
        {
            Closed(1, Anchor.AddMinutes(5), DirectionCandidate.BUY_CANDIDATE, 6500m, 6460m),
            Closed(2, Anchor.AddMinutes(10), DirectionCandidate.BUY_CANDIDATE, 100m, 101m),
        };

        PnLConfiguration pnlConfig = PnLConfiguration.Create(EsPnl(), quantity: 1);
        BacktestRiskConfiguration riskConfig = BacktestRiskConfiguration.Create(true, RiskDistanceConfiguration.Fixed(2m));

        BacktestRiskResult result = BacktestRiskResultBuilder.Build(
            positions, initialCapital: 100000m, EsRisk(), Policy(0.01m), pnlConfig, ExecutionCostConfiguration.Disabled(), riskConfig);

        Assert.Equal(1000m, result.Outcomes[0].RiskEvaluation.MaximumAllowedRisk); // 100000 * 1%
        Assert.Equal(-2000m, result.Outcomes[0].NetPnL);
        Assert.Equal(98000m, result.Outcomes[0].EquityAfter);

        Assert.Equal(980m, result.Outcomes[1].RiskEvaluation.MaximumAllowedRisk); // 98000 * 1%
    }

    // ── §17: rejection produces no execution, no cost, no PnL, no equity change ─────────────────────

    [Fact]
    public void RejectedPosition_ProducesNoExecutionNoCostNoPnlNoEquityChange()
    {
        var positions = new List<SimulatedPosition> { Closed(1, Anchor.AddMinutes(5), DirectionCandidate.BUY_CANDIDATE, 100m, 110m) };
        PnLConfiguration pnlConfig = PnLConfiguration.Create(EsPnl(), quantity: 1);
        // RiskDistance=None() -> InvalidRiskDistance -> always rejected.
        BacktestRiskConfiguration riskConfig = BacktestRiskConfiguration.Create(true, RiskDistanceConfiguration.None());

        BacktestRiskResult result = BacktestRiskResultBuilder.Build(
            positions, 100000m, EsRisk(), Policy(0.01m), pnlConfig, ExecutionCostConfiguration.Disabled(), riskConfig);

        PositionRiskOutcome outcome = result.Outcomes[0];
        Assert.False(outcome.RiskEvaluation.IsAllowed);
        Assert.Null(outcome.CostResult);
        Assert.Null(outcome.NetPnL);
        Assert.Equal(outcome.EquityBefore, outcome.EquityAfter);
        Assert.Equal(100000m, result.FinalEquity);
        Assert.Equal(0m, result.FinalNetPnL);
        Assert.Equal(0, result.AllowedCount);
        Assert.Equal(1, result.RejectedCount);
    }

    // ── §18/§20 Invariant 6: risk+cost disabled reproduces the Lot 14.6/14.7 baseline exactly ───────

    [Fact]
    public void RiskAndCostDisabled_MatchesLot146And147sOwnBuilders()
    {
        var positions = new List<SimulatedPosition>
        {
            Closed(1, Anchor.AddMinutes(5), DirectionCandidate.BUY_CANDIDATE, 100m, 102m),
            Closed(2, Anchor.AddMinutes(10), DirectionCandidate.SELL_CANDIDATE, 105m, 103m),
            Closed(3, Anchor.AddMinutes(15), DirectionCandidate.BUY_CANDIDATE, 103m, 101m),
        };
        PnLConfiguration pnlConfig = PnLConfiguration.Create(EsPnl(), quantity: 1, startingCapital: 100000m);

        // Independent computation via the Lot 14.6 builder directly.
        List<PositionPnLResult> pnlResults = positions.Select(p => PositionPnLCalculator.Calculate(p, pnlConfig)).ToList();
        BacktestPnLResult grossResult = BacktestPnLResultBuilder.Build(pnlResults, pnlConfig.StartingCapital);

        BacktestRiskResult riskResult = BacktestRiskResultBuilder.Build(
            positions, initialCapital: 100000m, EsRisk(), Policy(0.01m), pnlConfig,
            ExecutionCostConfiguration.Disabled(), BacktestRiskConfiguration.Disabled());

        Assert.Equal(grossResult.FinalGrossPnL, riskResult.FinalNetPnL);
        Assert.Equal(grossResult.MaximumDrawdown, riskResult.MaximumDrawdown);
        Assert.Equal(grossResult.FinalEquity, riskResult.FinalEquity);
    }

    // ── §17: run isolation ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RunA_RunB_RunA_ProduceIdenticalHashesForTheRepeatedRunA()
    {
        var positionsA = new List<SimulatedPosition> { Closed(1, Anchor.AddMinutes(5), DirectionCandidate.BUY_CANDIDATE, 100m, 105m) };
        var positionsB = new List<SimulatedPosition> { Closed(1, Anchor.AddMinutes(5), DirectionCandidate.SELL_CANDIDATE, 200m, 190m) };
        PnLConfiguration pnlConfig = PnLConfiguration.Create(EsPnl(), quantity: 1);
        BacktestRiskConfiguration riskConfig = BacktestRiskConfiguration.Create(true, RiskDistanceConfiguration.Fixed(2m));

        BacktestRiskResult runA1 = BacktestRiskResultBuilder.Build(positionsA, 100000m, EsRisk(), Policy(0.01m), pnlConfig, ExecutionCostConfiguration.Disabled(), riskConfig);
        BacktestRiskResultBuilder.Build(positionsB, 100000m, EsRisk(), Policy(0.01m), pnlConfig, ExecutionCostConfiguration.Disabled(), riskConfig);
        BacktestRiskResult runA2 = BacktestRiskResultBuilder.Build(positionsA, 100000m, EsRisk(), Policy(0.01m), pnlConfig, ExecutionCostConfiguration.Disabled(), riskConfig);

        Assert.Equal(runA1.DeterministicHash, runA2.DeterministicHash);
    }

    // ── §17: determinism ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SameInputs_ProduceTheSameDeterministicHash()
    {
        var positions = new List<SimulatedPosition> { Closed(1, Anchor.AddMinutes(5), DirectionCandidate.BUY_CANDIDATE, 100m, 105m) };
        PnLConfiguration pnlConfig = PnLConfiguration.Create(EsPnl(), quantity: 2);
        BacktestRiskConfiguration riskConfig = BacktestRiskConfiguration.Create(true, RiskDistanceConfiguration.Fixed(2m));

        BacktestRiskResult first = BacktestRiskResultBuilder.Build(positions, 100000m, EsRisk(), Policy(0.01m), pnlConfig, ExecutionCostConfiguration.Disabled(), riskConfig);
        BacktestRiskResult second = BacktestRiskResultBuilder.Build(positions, 100000m, EsRisk(), Policy(0.01m), pnlConfig, ExecutionCostConfiguration.Disabled(), riskConfig);

        Assert.Equal(first.DeterministicHash, second.DeterministicHash);
    }

    // ── §15: costs/PnL are computed on the risk-determined quantity, never the raw requested one ────

    [Fact]
    public void CostAndPnlQuantity_MatchesTheRiskDeterminedAllowedQuantity_NeverTheRawRequestedQuantity()
    {
        var positions = new List<SimulatedPosition> { Closed(1, Anchor.AddMinutes(5), DirectionCandidate.BUY_CANDIDATE, 100m, 110m) };
        // Requested=10 via PnLConfiguration.Quantity, but risk budget only affords 3.
        PnLConfiguration pnlConfig = PnLConfiguration.Create(EsPnl(), quantity: 10);
        RiskPolicy policyForThree = new(null, 300m, null, null, null, null, null, null, null, null); // RiskPerUnit=100 -> floor(300/100)=3
        BacktestRiskConfiguration riskConfig = BacktestRiskConfiguration.Create(true, RiskDistanceConfiguration.Fixed(2m));

        BacktestRiskResult result = BacktestRiskResultBuilder.Build(
            positions, 100000m, EsRisk(), policyForThree, pnlConfig, ExecutionCostConfiguration.Disabled(), riskConfig);

        PositionRiskOutcome outcome = result.Outcomes[0];
        Assert.Equal(3, outcome.RiskEvaluation.AllowedQuantity);
        Assert.NotNull(outcome.CostResult);
        Assert.Equal(3, outcome.CostResult!.Quantity);
        // GrossPnL at quantity 3: (110-100) * 50 * 3 = 1500.
        Assert.Equal(1500m, outcome.CostResult.GrossPnL);
    }

    // ── §17: non-Closed positions are excluded from the risk/equity walk, never fabricated ─────────

    [Fact]
    public void NonClosedPosition_IsExcludedFromEquityTracking_ButStillListedAsNotExecutable()
    {
        var notExecutable = new SimulatedPosition(
            2, PositionStatus.NotExecutable, "no trade", DirectionCandidate.NO_ACTION, Anchor, null, 1,
            null, null, null, null, null, null, null);
        var positions = new List<SimulatedPosition> { Closed(1, Anchor.AddMinutes(5), DirectionCandidate.BUY_CANDIDATE, 100m, 105m), notExecutable };

        PnLConfiguration pnlConfig = PnLConfiguration.Create(EsPnl(), quantity: 1);
        BacktestRiskConfiguration riskConfig = BacktestRiskConfiguration.Create(true, RiskDistanceConfiguration.Fixed(2m));

        BacktestRiskResult result = BacktestRiskResultBuilder.Build(
            positions, 100000m, EsRisk(), Policy(0.01m), pnlConfig, ExecutionCostConfiguration.Disabled(), riskConfig);

        Assert.Equal(2, result.Outcomes.Count);
        PositionRiskOutcome skipped = result.Outcomes.Single(o => o.PositionId == 2);
        Assert.Equal(PositionRiskReason.PositionNotExecutable, skipped.RiskEvaluation.Reason);
        Assert.Null(skipped.EquityBefore);
        Assert.Null(skipped.EquityAfter);
        Assert.Equal(1, result.RequestedCount); // only the Closed position was ever risk-evaluated
    }

    [Fact]
    public void Build_RejectsNonPositiveInitialCapital()
    {
        var positions = new List<SimulatedPosition>();
        Assert.Throws<ArgumentOutOfRangeException>(() => BacktestRiskResultBuilder.Build(
            positions, 0m, EsRisk(), Policy(0.01m), PnLConfiguration.Create(EsPnl()), ExecutionCostConfiguration.Disabled(), BacktestRiskConfiguration.Disabled()));
    }
}
