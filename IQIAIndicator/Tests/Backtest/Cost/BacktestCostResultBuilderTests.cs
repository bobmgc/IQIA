using System;
using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Backtest.Cost;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Pnl;
using IQIAIndicator.Engine.EntryTrigger;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Cost;

/// <summary>
/// Sprint 15.25 (Lot 14.7). <see cref="BacktestCostResultBuilder"/> - net equity curve, net drawdown
/// (mirroring Lot 14.6's own worked examples, substituting NetPnL for GrossPnL), cost-total aggregation,
/// empty input, and the zero-cost equivalence to <see cref="BacktestPnLResultBuilder"/>.
/// </summary>
public sealed class BacktestCostResultBuilderTests
{
    private static readonly DateTime Anchor = new(2026, 1, 5, 14, 30, 0, DateTimeKind.Utc);

    private static PositionCostResult ClosedCost(
        int id, DateTime exitTs, decimal netPnL,
        decimal commission = 0m, decimal fees = 0m, decimal spreadCost = 0m, decimal slippageCost = 0m)
    {
        var cost = new ExecutionCost(commission, fees, spreadCost, slippageCost, commission + fees + spreadCost + slippageCost);
        decimal grossPnL = netPnL + cost.TotalCost;
        return new PositionCostResult(
            id, PositionStatus.Closed, DirectionCandidate.BUY_CANDIDATE, Anchor, 100m, 100m, exitTs, 100m, 100m,
            grossPnL, cost, netPnL, "USD", 1);
    }

    // ── mirrors Lot 14.6's own MaximumDrawdown worked example, using NetPnL instead of GrossPnL ─────

    [Fact]
    public void MaximumNetDrawdown_MatchesTheSameWorkedExampleAsLot146()
    {
        // Same 100000/101000/99000/98000/103000 sequence as the Lot 14.6 brief -> MaximumDrawdown = -3000.
        var positions = new List<PositionCostResult>
        {
            ClosedCost(1, Anchor.AddMinutes(5), 1000m),
            ClosedCost(2, Anchor.AddMinutes(10), -2000m),
            ClosedCost(3, Anchor.AddMinutes(15), -1000m),
            ClosedCost(4, Anchor.AddMinutes(20), 5000m),
        };

        BacktestCostResult result = BacktestCostResultBuilder.Build(positions, startingCapital: 100000m);

        Assert.Equal(-3000m, result.MaximumNetDrawdown);
        Assert.Equal(103000m, result.FinalNetEquity);
        Assert.Equal(3000m, result.FinalNetPnL);
    }

    [Fact]
    public void SeveralConsecutiveLosses_MaximumNetDrawdownAccumulates()
    {
        var positions = new List<PositionCostResult>
        {
            ClosedCost(1, Anchor.AddMinutes(5), -100m),
            ClosedCost(2, Anchor.AddMinutes(10), -200m),
            ClosedCost(3, Anchor.AddMinutes(15), -300m),
        };

        BacktestCostResult result = BacktestCostResultBuilder.Build(positions, null);

        Assert.Equal(-600m, result.MaximumNetDrawdown);
    }

    [Fact]
    public void NoPositions_ProducesZeroEverything_NeverThrows()
    {
        BacktestCostResult result = BacktestCostResultBuilder.Build(new List<PositionCostResult>(), startingCapital: 100000m);

        Assert.Equal(0m, result.FinalNetPnL);
        Assert.Equal(0m, result.MaximumNetDrawdown);
        Assert.Equal(0m, result.TotalCost);
        Assert.Empty(result.NetEquityCurve);
    }

    [Fact]
    public void EquityCurve_IsOrderedByExitTimestamp_ThenPositionIdAsATieBreak()
    {
        var positions = new List<PositionCostResult>
        {
            ClosedCost(3, Anchor.AddMinutes(5), 10m),
            ClosedCost(1, Anchor.AddMinutes(5), 20m), // same timestamp, lower id
            ClosedCost(2, Anchor.AddMinutes(1), 30m), // earliest
        };

        BacktestCostResult result = BacktestCostResultBuilder.Build(positions, null);

        Assert.Equal(2, result.NetEquityCurve[0].PositionId);
        Assert.Equal(1, result.NetEquityCurve[1].PositionId);
        Assert.Equal(3, result.NetEquityCurve[2].PositionId);
    }

    // ── cost-total aggregation across multiple positions ────────────────────────────────────────────

    [Fact]
    public void TotalCost_AggregatesEveryComponentAcrossAllClosedPositions()
    {
        var positions = new List<PositionCostResult>
        {
            ClosedCost(1, Anchor.AddMinutes(5), 100m, commission: 4m, fees: 0.2m, spreadCost: 2.5m, slippageCost: 2.5m),
            ClosedCost(2, Anchor.AddMinutes(10), 200m, commission: 4m, fees: 0.2m, spreadCost: 2.5m, slippageCost: 2.5m),
        };

        BacktestCostResult result = BacktestCostResultBuilder.Build(positions, null);

        Assert.Equal(8m, result.TotalCommission);
        Assert.Equal(0.4m, result.TotalFees);
        Assert.Equal(5m, result.TotalSpreadCost);
        Assert.Equal(5m, result.TotalSlippageCost);
        Assert.Equal(18.4m, result.TotalCost);
    }

    [Fact]
    public void Build_RejectsNonPositiveStartingCapital()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BacktestCostResultBuilder.Build(new List<PositionCostResult>(), 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => BacktestCostResultBuilder.Build(new List<PositionCostResult>(), -1m));
    }

    // ── §8/§9: zero-cost equivalence to Lot 14.6's own builder, end to end ──────────────────────────

    [Fact]
    public void ZeroCost_NetEquityCurve_IsNumericallyIdenticalToLot146sGrossEquityCurve()
    {
        var mesPositions = new List<SimulatedPosition>
        {
            new(1, PositionStatus.Closed, null, DirectionCandidate.BUY_CANDIDATE, Anchor, 100m, 0, Anchor.AddMinutes(5), 102m, 10, ExitReason.TimeHorizon, 10, 2m, 0.02),
            new(2, PositionStatus.Closed, null, DirectionCandidate.SELL_CANDIDATE, Anchor.AddMinutes(1), 105m, 1, Anchor.AddMinutes(10), 103m, 11, ExitReason.TimeHorizon, 10, 2m, 0.019),
            new(3, PositionStatus.Closed, null, DirectionCandidate.BUY_CANDIDATE, Anchor.AddMinutes(2), 103m, 2, Anchor.AddMinutes(15), 101m, 12, ExitReason.TimeHorizon, 10, -2m, -0.019),
        };

        PnLConfiguration pnlConfig = PnLConfiguration.Create(InstrumentPnLSpecification.Create("MES", 5m, "USD"), quantity: 1, startingCapital: 100000m);
        List<PositionPnLResult> pnlResults = mesPositions.Select(p => PositionPnLCalculator.Calculate(p, pnlConfig)).ToList();
        BacktestPnLResult grossResult = BacktestPnLResultBuilder.Build(pnlResults, pnlConfig.StartingCapital);

        List<PositionCostResult> costResults = mesPositions
            .Zip(pnlResults, (p, r) => PositionCostCalculator.Calculate(p, r, pnlConfig, ExecutionCostConfiguration.Disabled()))
            .ToList();
        BacktestCostResult netResult = BacktestCostResultBuilder.Build(costResults, pnlConfig.StartingCapital);

        Assert.Equal(grossResult.FinalGrossPnL, netResult.FinalNetPnL);
        Assert.Equal(grossResult.MaximumDrawdown, netResult.MaximumNetDrawdown);
        Assert.Equal(grossResult.FinalEquity, netResult.FinalNetEquity);
        Assert.Equal(grossResult.EquityCurve.Count, netResult.NetEquityCurve.Count);

        for (int i = 0; i < grossResult.EquityCurve.Count; i++)
        {
            Assert.Equal(grossResult.EquityCurve[i].CumulativePnL, netResult.NetEquityCurve[i].CumulativeNetPnL);
            Assert.Equal(grossResult.EquityCurve[i].Equity, netResult.NetEquityCurve[i].NetEquity);
            Assert.Equal(grossResult.EquityCurve[i].Drawdown, netResult.NetEquityCurve[i].NetDrawdown);
        }
    }
}
