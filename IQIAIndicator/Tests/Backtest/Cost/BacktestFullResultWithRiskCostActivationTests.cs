using System.Linq;
using IQIAIndicator.Backtest;
using IQIAIndicator.Backtest.Cost;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Measurement;
using IQIAIndicator.Backtest.Pnl;
using IQIAIndicator.Backtest.Risk;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Tests.BacktestTests;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Cost;

/// <summary>
/// Sprint 15.25 (Lot 14.10, P0-2). The cost model's FORMULA was already validated at the unit level by
/// Lot 14.7's <c>PositionCostCalculatorTests</c> - what this lot proves, and what did not previously exist,
/// is that the SAME activation actually reaches the calibration lab's own entry point,
/// <see cref="BacktestEngine.RunFullBacktestWithRisk"/> (the method
/// <c>Backtest.Calibration.CalibrationExperimentRunner</c> calls): a caller can genuinely choose
/// "Costs OFF" (historical behaviour preserved) or "Costs ON + explicit configuration" (GrossPnL and NetPnL
/// genuinely diverge), through the exact same surface a calibration experiment uses.
///
/// Every cost value below is an explicit TEST FIXTURE (brief P0-2 point 9: "Les valeurs de test doivent
/// rester clairement identifiées comme FIXTURES DE TEST") - none of it is a broker schedule, none of it is
/// promoted as a production default (which stays <see cref="ExecutionCostConfiguration.Disabled"/>,
/// unchanged by this lot).
/// </summary>
public sealed class BacktestFullResultWithRiskCostActivationTests
{
    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("ES", TickSize: 0.25m, TickValue: 12.5m, PointValue: 50m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    private static BacktestScenario ScenarioFor(HistoricalSeries series) =>
        BacktestScenario.Create(series, new BacktestWindow("W", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)), 50_000m, Spec(), Policy());

    private static MeasurementConfiguration Measurement() => MeasurementConfiguration.Create(10, new[] { 0.001 });

    private static ExecutionConfiguration Exec() => ExecutionConfiguration.Create(10);

    private static PnLConfiguration Pnl() =>
        PnLConfiguration.Create(InstrumentPnLSpecification.Create("ES", 50m, "USD"), quantity: 1, startingCapital: 50_000m);

    /// <summary>TEST FIXTURE ONLY (see class doc comment) - not a broker schedule.</summary>
    private static ExecutionCostConfiguration EnabledTestCosts() => ExecutionCostConfiguration.Create(
        enabled: true,
        slippage: SlippageConfiguration.Fixed(0.25m),
        spread: SpreadConfiguration.Fixed(0.5m),
        commission: CommissionConfiguration.Create(perOrder: 2m, perUnit: 0.5m),
        fees: FeesConfiguration.Create(perOrder: 0.25m));

    // ── "Costs OFF" preserves the exact historical (Lot 14.6/14.8) behaviour ────────────────────────────

    [Fact]
    public void CostsOff_NetPnLEqualsGrossPnL_ForEveryAllowedPosition()
    {
        BacktestScenario scenario = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(220, seed: 31UL));
        BacktestRiskConfiguration riskConfig = BacktestRiskConfiguration.Create(true, RiskDistanceConfiguration.Fixed(2m));

        BacktestFullResultWithRisk result = new BacktestEngine().RunFullBacktestWithRisk(
            scenario, 128, Measurement(), Exec(), Pnl(), ExecutionCostConfiguration.Disabled(), riskConfig);

        int checkedCount = 0;
        foreach (var outcome in result.RiskResult.Outcomes.Where(o => o.RiskEvaluation.IsAllowed && o.NetPnL is not null))
        {
            checkedCount++;
            Assert.Equal(outcome.CostResult!.GrossPnL, outcome.NetPnL);
            Assert.Equal(0m, outcome.CostResult.Cost!.TotalCost);
        }

        Assert.True(checkedCount > 0, "Expected at least one allowed, priced position for this scenario.");
    }

    // ── "Costs ON + explicit configuration" - GrossPnL and NetPnL genuinely diverge ─────────────────────

    [Fact]
    public void CostsOn_NetPnLDiffersFromGrossPnL_ByExactlyTheConfiguredCost_ForEveryAllowedPosition()
    {
        BacktestScenario scenario = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(220, seed: 31UL));
        BacktestRiskConfiguration riskConfig = BacktestRiskConfiguration.Create(true, RiskDistanceConfiguration.Fixed(2m));
        ExecutionCostConfiguration costs = EnabledTestCosts();

        BacktestFullResultWithRisk result = new BacktestEngine().RunFullBacktestWithRisk(
            scenario, 128, Measurement(), Exec(), Pnl(), costs, riskConfig);

        int checkedCount = 0;
        foreach (var outcome in result.RiskResult.Outcomes.Where(o => o.RiskEvaluation.IsAllowed && o.NetPnL is not null))
        {
            checkedCount++;
            decimal totalCost = outcome.CostResult!.Cost!.TotalCost;

            // brief P0-2 point 8: NetPnL = GrossPnL - TotalCost, using this project's own existing formula
            // (Backtest.Cost.PositionCostCalculator), never a newly invented one.
            Assert.Equal(outcome.CostResult.GrossPnL!.Value - totalCost, outcome.NetPnL!.Value);

            // The whole point of P0-2: with a non-zero cost configuration, Net must actually differ from
            // Gross - never silently identical because some component was left at its zero default.
            Assert.True(totalCost > 0m, "Expected a strictly positive TotalCost with every cost component configured non-zero.");
            Assert.NotEqual(outcome.CostResult.GrossPnL!.Value, outcome.NetPnL!.Value);
        }

        Assert.True(checkedCount > 0, "Expected at least one allowed, priced position for this scenario.");
    }

    [Fact]
    public void CostsOn_FinalNetPnL_DiffersFromFinalGrossPnL_AtTheAggregateLevel()
    {
        BacktestScenario scenario = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(220, seed: 31UL));
        BacktestRiskConfiguration riskConfig = BacktestRiskConfiguration.Create(true, RiskDistanceConfiguration.Fixed(2m));

        BacktestFullResultWithRisk withCosts = new BacktestEngine().RunFullBacktestWithRisk(
            scenario, 128, Measurement(), Exec(), Pnl(), EnabledTestCosts(), riskConfig);
        BacktestFullResultWithRisk withoutCosts = new BacktestEngine().RunFullBacktestWithRisk(
            scenario, 128, Measurement(), Exec(), Pnl(), ExecutionCostConfiguration.Disabled(), riskConfig);

        Assert.True(withCosts.RiskResult.RequestedCount > 0, "Expected at least one Closed position for this scenario.");

        // Same signals/positions either way (cost activation must never change WHICH positions exist) -
        // only their net economics differ.
        Assert.Equal(withoutCosts.SignalResult.DeterministicHash, withCosts.SignalResult.DeterministicHash);
        Assert.Equal(withoutCosts.ExecutionResult.DeterministicHash, withCosts.ExecutionResult.DeterministicHash);

        Assert.NotEqual(withoutCosts.RiskResult.FinalNetPnL, withCosts.RiskResult.FinalNetPnL);
        // Costs only ever reduce the net result (every configured component above is non-negative).
        Assert.True(withCosts.RiskResult.FinalNetPnL < withoutCosts.RiskResult.FinalNetPnL);
    }

    // ── Chain wiring (brief P0-2 point 5): signal -> execution -> gross PnL -> transaction costs -> net
    // PnL -> equity, verified end to end through the Risk/Calibration-facing surface ────────────────────

    [Fact]
    public void CostsOn_EquityAfter_ReflectsNetPnL_NeverGrossPnL()
    {
        BacktestScenario scenario = ScenarioFor(BacktestTestSeriesBuilder.MeanRevertingOu(220, seed: 31UL));
        BacktestRiskConfiguration riskConfig = BacktestRiskConfiguration.Create(true, RiskDistanceConfiguration.Fixed(2m));

        BacktestFullResultWithRisk result = new BacktestEngine().RunFullBacktestWithRisk(
            scenario, 128, Measurement(), Exec(), Pnl(), EnabledTestCosts(), riskConfig);

        // Mirrors BacktestRiskResultBuilder.Build's OWN accumulation shape exactly (its doc comment is
        // explicit about this): cumulativeNetPnL starts at 0m and only ever sums NetPnL values;
        // EquityAfter is always a FRESH single addition (InitialCapital + cumulativeNetPnL), never an
        // incrementally-updated running equity variable - the two are mathematically equal but not
        // guaranteed bit-identical under finite decimal precision, and production picked the former.
        decimal cumulativeNetPnL = 0m;
        int checkedCount = 0;
        foreach (var outcome in result.RiskResult.Outcomes.Where(o => o.RiskEvaluation.IsAllowed && o.NetPnL is not null)
            .OrderBy(o => o.ExitTimestamp).ThenBy(o => o.PositionId))
        {
            checkedCount++;
            cumulativeNetPnL += outcome.NetPnL!.Value;
            Assert.Equal(scenario.InitialCapital + cumulativeNetPnL, outcome.EquityAfter);
        }

        Assert.True(checkedCount > 0, "Expected at least one allowed, priced position for this scenario.");
        Assert.Equal(scenario.InitialCapital + cumulativeNetPnL, result.RiskResult.FinalEquity);
    }
}
