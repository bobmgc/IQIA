using System;
using IQIAIndicator.Backtest.Cost;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Risk;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Risk;
using Xunit;

namespace IQIAIndicator.Tests.BacktestTests.Risk;

/// <summary>
/// Sprint 15.25 (Lot 14.8, brief §5/§6/§7/§8/§9/§10/§11/§12/§17). <see cref="PositionRiskEvaluator"/> - risk
/// per trade, position sizing, risk-per-unit, risk distance, exposure, quantity constraints, Long/Short,
/// instrument-awareness, rejection, and determinism, all for ONE candidate position.
/// </summary>
public sealed class PositionRiskEvaluatorTests
{
    private static readonly DateTime EntryTs = new(2026, 1, 5, 14, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime ExitTs = new(2026, 1, 5, 15, 20, 0, DateTimeKind.Utc);

    private static SimulatedPosition Closed(DirectionCandidate direction, decimal entry, decimal exit = 0m)
    {
        decimal usedExit = exit == 0m ? entry : exit;
        decimal move = direction == DirectionCandidate.BUY_CANDIDATE ? usedExit - entry : entry - usedExit;

        return new SimulatedPosition(
            PositionId: 1, PositionStatus.Closed, null, direction,
            EntryTs, entry, EntryBarIndex: 0,
            ExitTs, usedExit, ExitBarIndex: 10, ExitReason.TimeHorizon,
            HoldingBars: 10, GrossPriceMove: move, Return: 0.0);
    }

    private static InstrumentRiskSpecification Mes(decimal? contractMultiplier = null, int maxQuantity = 50) =>
        new("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, MinQuantity: 1, MaxQuantity: maxQuantity, QuantityStep: 1, ContractMultiplier: contractMultiplier);

    private static InstrumentRiskSpecification Es(decimal? contractMultiplier = null) =>
        new("ES", TickSize: 0.25m, TickValue: 12.5m, PointValue: 50m, MinQuantity: 1, MaxQuantity: 50, QuantityStep: 1, ContractMultiplier: contractMultiplier);

    private static RiskPolicy Policy(decimal? riskPerTradePercent = null, decimal? riskPerTradeAmount = null, int? maxPositionSize = null) =>
        new(riskPerTradePercent, riskPerTradeAmount, null, null, null, null, null, null, null, maxPositionSize);

    private static AccountState Account(decimal equity = 100000m, decimal? initialCapital = null, decimal? peak = null)
    {
        decimal capital = initialCapital ?? equity;
        return new AccountState(capital, equity, null, peak ?? equity, capital, equity - capital, 0m, 0m);
    }

    private static BacktestRiskConfiguration Risk(decimal riskDistance = 2m, decimal? maxExposure = null, bool enabled = true) =>
        BacktestRiskConfiguration.Create(enabled, RiskDistanceConfiguration.Fixed(riskDistance), maxExposure);

    private static ExecutionCostConfiguration NoCosts() => ExecutionCostConfiguration.Disabled();

    // ── §4/§17: default/disabled configuration reproduces historical behaviour ─────────────────────

    [Fact]
    public void Disabled_AllowedQuantityEqualsRequestedQuantity_NothingElseEvaluated()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m);
        RiskEvaluationResult result = PositionRiskEvaluator.Evaluate(
            position, requestedQuantity: 7, Account(), Mes(), Policy(0.01m), BacktestRiskConfiguration.Disabled(), NoCosts());

        Assert.True(result.IsAllowed);
        Assert.Equal(7, result.AllowedQuantity);
        Assert.Null(result.RiskPerUnit);
        Assert.Null(result.MaximumAllowedRisk);
        Assert.Equal(PositionRiskReason.Allowed, result.Reason);
    }

    // ── §5: RiskPerTrade worked example - Capital=100000, Risk=1% -> MaximumRisk=1000 ───────────────

    [Fact]
    public void MaximumAllowedRisk_MatchesTheBriefsOwnWorkedExample_OnePercentOfEquity()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m);
        RiskEvaluationResult result = PositionRiskEvaluator.Evaluate(
            position, 100, Account(equity: 100000m), Mes(), Policy(riskPerTradePercent: 0.01m), Risk(), NoCosts());

        Assert.Equal(1000m, result.MaximumAllowedRisk);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.01, 1000.0)]
    [InlineData(0.02, 2000.0)]
    public void MaximumAllowedRisk_ScalesWithConfiguredRiskPerTradePercent(double riskPercent, double expectedMaxRisk)
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m);
        RiskEvaluationResult result = PositionRiskEvaluator.Evaluate(
            position, 100, Account(equity: 100000m), Mes(), Policy(riskPerTradePercent: (decimal)riskPercent), Risk(), NoCosts());

        Assert.Equal((decimal)expectedMaxRisk, result.MaximumAllowedRisk);
        if (expectedMaxRisk == 0.0)
        {
            Assert.False(result.IsAllowed);
            Assert.Equal(PositionRiskReason.RiskLimitExceeded, result.Reason);
        }
    }

    // ── §7: RiskPerUnit / Position sizing worked example from the brief itself ──────────────────────

    [Fact]
    public void RiskPerUnitAndPositionSizing_MatchTheBriefsOwnWorkedExample()
    {
        // Entry=100, Stop=98 (RiskDistance=2), PointValue=5 -> RiskPerUnit=10. MaximumRisk=100 -> AllowedQuantity=10.
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m);
        BacktestRiskConfiguration risk = Risk(riskDistance: 2m);
        RiskPolicy policy = Policy(riskPerTradeAmount: 100m);

        RiskEvaluationResult result = PositionRiskEvaluator.Evaluate(position, 50, Account(), Mes(), policy, risk, NoCosts());

        Assert.Equal(10m, result.RiskPerUnit);
        Assert.Equal(100m, result.MaximumAllowedRisk);
        Assert.Equal(10, result.AllowedQuantity);
        Assert.Equal(100m, result.TotalEstimatedRisk); // 10 (RiskPerUnit) * 10 (AllowedQuantity)
        Assert.True(result.IsAllowed);
    }

    // ── §8: risk distance ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MissingRiskDistance_None_IsExplicitlyRejected_NeverInventsAStop()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m);
        BacktestRiskConfiguration risk = BacktestRiskConfiguration.Create(true, RiskDistanceConfiguration.None());

        RiskEvaluationResult result = PositionRiskEvaluator.Evaluate(position, 5, Account(), Mes(), Policy(0.01m), risk, NoCosts());

        Assert.False(result.IsAllowed);
        Assert.Equal(0, result.AllowedQuantity);
        Assert.Equal(PositionRiskReason.InvalidRiskDistance, result.Reason);
    }

    [Fact]
    public void ZeroRiskDistance_IsExplicitlyRejected()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m);
        BacktestRiskConfiguration risk = BacktestRiskConfiguration.Create(true, RiskDistanceConfiguration.Fixed(0m));

        RiskEvaluationResult result = PositionRiskEvaluator.Evaluate(position, 5, Account(), Mes(), Policy(0.01m), risk, NoCosts());

        Assert.Equal(PositionRiskReason.InvalidRiskDistance, result.Reason);
    }

    [Fact]
    public void ValidRiskDistance_ProducesAnAcceptedSizing()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m);
        RiskEvaluationResult result = PositionRiskEvaluator.Evaluate(position, 5, Account(), Mes(), Policy(0.01m), Risk(2m), NoCosts());

        Assert.True(result.IsAllowed);
        Assert.NotNull(result.RiskPerUnit);
    }

    // Note: a NEGATIVE risk distance cannot be expressed at all - RiskDistanceConfiguration.Fixed rejects
    // it at construction (see RiskDistanceConfigurationTests.Fixed_RejectsNegativeDistance), which is the
    // correct place to catch it (brief §17 "negative distance" is exercised there, not here).

    // ── §7/§12: Long/Short produce the identical absolute risk ──────────────────────────────────────

    [Fact]
    public void Long_And_Short_ProduceTheIdenticalAbsoluteRiskPerUnitAndSizing()
    {
        SimulatedPosition longPosition = Closed(DirectionCandidate.BUY_CANDIDATE, 100m);
        SimulatedPosition shortPosition = Closed(DirectionCandidate.SELL_CANDIDATE, 100m);
        RiskPolicy policy = Policy(riskPerTradeAmount: 100m);

        RiskEvaluationResult longResult = PositionRiskEvaluator.Evaluate(longPosition, 50, Account(), Mes(), policy, Risk(2m), NoCosts());
        RiskEvaluationResult shortResult = PositionRiskEvaluator.Evaluate(shortPosition, 50, Account(), Mes(), policy, Risk(2m), NoCosts());

        Assert.Equal(longResult.RiskPerUnit, shortResult.RiskPerUnit);
        Assert.Equal(longResult.AllowedQuantity, shortResult.AllowedQuantity);
        Assert.Equal(longResult.MaximumAllowedRisk, shortResult.MaximumAllowedRisk);
    }

    // ── §9/§17: invalid instrument ────────────────────────────────────────────────────────────────

    [Fact]
    public void InvalidInstrumentSpecification_IsRejectedExplicitly()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m);
        // MaxQuantity < MinQuantity makes InstrumentRiskSpecification.IsValid false.
        var invalidInstrument = new InstrumentRiskSpecification("MES", 0.25m, 1.25m, 5m, MinQuantity: 10, MaxQuantity: 1, QuantityStep: 1);

        RiskEvaluationResult result = PositionRiskEvaluator.Evaluate(position, 5, Account(), invalidInstrument, Policy(0.01m), Risk(2m), NoCosts());

        Assert.False(result.IsAllowed);
        Assert.Equal(PositionRiskReason.InvalidInstrument, result.Reason);
    }

    // ── §9/§17: invalid risk percentage (negative) is gracefully rejected, never a crash ────────────

    [Fact]
    public void NegativeRiskPerTradePercent_IsGracefullyRejected_NeverThrows()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m);
        RiskEvaluationResult result = PositionRiskEvaluator.Evaluate(
            position, 5, Account(), Mes(), Policy(riskPerTradePercent: -0.01m), Risk(2m), NoCosts());

        Assert.False(result.IsAllowed);
        Assert.Equal(0, result.AllowedQuantity);
        Assert.Equal(PositionRiskReason.RiskLimitExceeded, result.Reason);
    }

    // ── §11/§17: requested quantity = 0 is rejected as an invalid input (mirrors PnLConfiguration.Create) ─

    [Fact]
    public void RequestedQuantity_Zero_IsRejectedAsInvalidInput()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PositionRiskEvaluator.Evaluate(position, 0, Account(), Mes(), Policy(0.01m), Risk(2m), NoCosts()));
    }

    // AllowedQuantity resolving to 0 as an OUTPUT (risk budget rounds down to zero) is a distinct, gracefully
    // handled outcome - see ZeroRiskBudget_RoundsDownToZero_IsRejectedGracefully below.

    [Fact]
    public void ZeroRiskBudget_RoundsDownToZero_IsRejectedGracefully()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m);
        // RiskPerUnit = 2 * 5 = 10; a $5 budget affords 0 whole contracts.
        RiskEvaluationResult result = PositionRiskEvaluator.Evaluate(
            position, 5, Account(), Mes(), Policy(riskPerTradeAmount: 5m), Risk(2m), NoCosts());

        Assert.False(result.IsAllowed);
        Assert.Equal(0, result.AllowedQuantity);
        Assert.Equal(PositionRiskReason.ZeroRisk, result.Reason);
    }

    // ── §11/§17: requested > allowed / allowed > requested ──────────────────────────────────────────

    [Fact]
    public void RequestedGreaterThanRiskAllowed_FinalIsTheSmallerRiskAllowedValue()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m);
        RiskEvaluationResult result = PositionRiskEvaluator.Evaluate(
            position, requestedQuantity: 10, Account(), Mes(), Policy(riskPerTradeAmount: 60m), Risk(2m), NoCosts());

        Assert.Equal(10, result.RequestedQuantity);
        Assert.Equal(6, result.RiskConstrainedQuantity); // floor(60/10)
        Assert.Equal(6, result.AllowedQuantity);
    }

    [Fact]
    public void RiskAllowedGreaterThanRequested_FinalIsTheSmallerRequestedValue()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m);
        RiskEvaluationResult result = PositionRiskEvaluator.Evaluate(
            position, requestedQuantity: 3, Account(), Mes(maxQuantity: 1000), Policy(riskPerTradeAmount: 1000m), Risk(2m), NoCosts());

        Assert.Equal(3, result.RequestedQuantity);
        Assert.Equal(100, result.RiskConstrainedQuantity); // floor(1000/10)
        Assert.Equal(3, result.AllowedQuantity);
    }

    // ── §10/§17: exposure ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Exposure_BelowLimit_DoesNotConstrainTheQuantity()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m);
        // ExecutedPrice=100 (no slippage/spread), ContractMultiplier=1 -> exposure/unit=100. MaxExposure=10000 -> allows 100.
        RiskEvaluationResult result = PositionRiskEvaluator.Evaluate(
            position, 5, Account(), Mes(contractMultiplier: 1m), Policy(riskPerTradeAmount: 1000m), Risk(2m, maxExposure: 10000m), NoCosts());

        Assert.Equal(5, result.AllowedQuantity); // requested (5) is the binding constraint here
        Assert.Equal(100, result.ExposureConstrainedQuantity);
        Assert.Equal(500m, result.Exposure); // 100 * 5 * 1
    }

    [Fact]
    public void Exposure_ExactlyAtLimit_AllowsExactlyThatQuantity()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m);
        // MaxExposure=500 / (100*1) = 5 exactly.
        RiskEvaluationResult result = PositionRiskEvaluator.Evaluate(
            position, 5, Account(), Mes(contractMultiplier: 1m), Policy(riskPerTradeAmount: 1000m), Risk(2m, maxExposure: 500m), NoCosts());

        Assert.Equal(5, result.ExposureConstrainedQuantity);
        Assert.Equal(5, result.AllowedQuantity);
        Assert.True(result.Exposure <= result.MaxExposure);
    }

    [Fact]
    public void Exposure_AboveLimit_ReducesTheQuantity()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m);
        // MaxExposure=250 / (100*1) = 2 (floor).
        RiskEvaluationResult result = PositionRiskEvaluator.Evaluate(
            position, 10, Account(), Mes(contractMultiplier: 1m), Policy(riskPerTradeAmount: 1000m), Risk(2m, maxExposure: 250m), NoCosts());

        Assert.Equal(2, result.ExposureConstrainedQuantity);
        Assert.Equal(2, result.AllowedQuantity);
    }

    [Fact]
    public void Exposure_SoTightItRejectsEntirely_ReportsExposureLimitExceeded()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m);
        RiskEvaluationResult result = PositionRiskEvaluator.Evaluate(
            position, 10, Account(), Mes(contractMultiplier: 1m), Policy(riskPerTradeAmount: 1000m), Risk(2m, maxExposure: 50m), NoCosts());

        Assert.False(result.IsAllowed);
        Assert.Equal(0, result.AllowedQuantity);
        Assert.Equal(PositionRiskReason.ExposureLimitExceeded, result.Reason);
    }

    [Fact]
    public void NullContractMultiplier_DefaultsToOne_NeverFabricatesADifferentValue()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m);
        RiskEvaluationResult withNullMultiplier = PositionRiskEvaluator.Evaluate(
            position, 5, Account(), Mes(contractMultiplier: null), Policy(riskPerTradeAmount: 1000m), Risk(2m, maxExposure: 10000m), NoCosts());
        RiskEvaluationResult withExplicitOne = PositionRiskEvaluator.Evaluate(
            position, 5, Account(), Mes(contractMultiplier: 1m), Policy(riskPerTradeAmount: 1000m), Risk(2m, maxExposure: 10000m), NoCosts());

        Assert.Equal(withExplicitOne.Exposure, withNullMultiplier.Exposure);
    }

    // ── §11/§17: combined constraints - the brief's own two worked examples ────────────────────────

    [Fact]
    public void CombinedConstraints_Example1_RiskSixExposureEight_FinalIsSix()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m);
        // RiskPerUnit=10 (RiskDistance=2 * PointValue=5). Risk budget=60 -> RiskAllows=6. Exposure/unit=100*1
        // -> MaxExposure=800 -> ExposureAllows=8. Requested=10.
        RiskEvaluationResult result = PositionRiskEvaluator.Evaluate(
            position, requestedQuantity: 10, Account(), Mes(contractMultiplier: 1m),
            Policy(riskPerTradeAmount: 60m), Risk(2m, maxExposure: 800m), NoCosts());

        Assert.Equal(10, result.RequestedQuantity);
        Assert.Equal(6, result.RiskConstrainedQuantity);
        Assert.Equal(8, result.ExposureConstrainedQuantity);
        Assert.Equal(6, result.AllowedQuantity);
    }

    [Fact]
    public void CombinedConstraints_Example2_RiskTenExposureFour_FinalIsFour()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m);
        // Risk budget=100 -> RiskAllows=10. MaxExposure=400 -> ExposureAllows=4. Requested=10.
        RiskEvaluationResult result = PositionRiskEvaluator.Evaluate(
            position, requestedQuantity: 10, Account(), Mes(contractMultiplier: 1m),
            Policy(riskPerTradeAmount: 100m), Risk(2m, maxExposure: 400m), NoCosts());

        Assert.Equal(10, result.RiskConstrainedQuantity);
        Assert.Equal(4, result.ExposureConstrainedQuantity);
        Assert.Equal(4, result.AllowedQuantity);
    }

    // ── §17: instrument-awareness - never hardcode MES ──────────────────────────────────────────────

    [Fact]
    public void DifferentInstruments_ProduceProportionalRiskPerUnit_NeverAHardcodedValue()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m);
        RiskPolicy policy = Policy(riskPerTradeAmount: 10000m);

        RiskEvaluationResult mesResult = PositionRiskEvaluator.Evaluate(position, 100, Account(), Mes(), policy, Risk(2m), NoCosts());
        RiskEvaluationResult esResult = PositionRiskEvaluator.Evaluate(position, 100, Account(), Es(), policy, Risk(2m), NoCosts());

        Assert.Equal(10m, mesResult.RiskPerUnit);   // 2 * 5
        Assert.Equal(100m, esResult.RiskPerUnit);   // 2 * 50 - proportional to PointValue, not hardcoded
        Assert.Equal(esResult.RiskPerUnit, mesResult.RiskPerUnit! * 10m);
    }

    // ── §20 Invariants, verified directly against arbitrary combined constraints ────────────────────

    [Theory]
    [InlineData(10, 60.0, 800.0)]
    [InlineData(3, 1000.0, 10000.0)]
    [InlineData(50, 30.0, 150.0)]
    public void Invariants_AlwaysHoldForAnyAcceptedPosition(int requested, double riskAmount, double maxExposure)
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 100m);
        RiskEvaluationResult result = PositionRiskEvaluator.Evaluate(
            position, requested, Account(), Mes(contractMultiplier: 1m, maxQuantity: 1000),
            Policy(riskPerTradeAmount: (decimal)riskAmount), Risk(2m, maxExposure: (decimal)maxExposure), NoCosts());

        // Invariant 1: FinalQuantity <= RequestedQuantity.
        Assert.True(result.AllowedQuantity <= result.RequestedQuantity);

        if (result.IsAllowed)
        {
            // Invariant 3: EstimatedRisk <= MaximumAllowedRisk.
            Assert.True(result.TotalEstimatedRisk <= result.MaximumAllowedRisk);
            // Invariant 4: Exposure <= MaxExposure.
            Assert.True(result.Exposure <= result.MaxExposure);
        }
    }

    // ── §17: determinism ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SameInputs_ProduceTheExactSameScalarResults()
    {
        SimulatedPosition position = Closed(DirectionCandidate.BUY_CANDIDATE, 6500m);
        RiskEvaluationResult first = PositionRiskEvaluator.Evaluate(position, 10, Account(), Mes(), Policy(0.01m), Risk(2m, 10000m), NoCosts());
        RiskEvaluationResult second = PositionRiskEvaluator.Evaluate(position, 10, Account(), Mes(), Policy(0.01m), Risk(2m, 10000m), NoCosts());

        Assert.Equal(first.IsAllowed, second.IsAllowed);
        Assert.Equal(first.AllowedQuantity, second.AllowedQuantity);
        Assert.Equal(first.RiskPerUnit, second.RiskPerUnit);
        Assert.Equal(first.TotalEstimatedRisk, second.TotalEstimatedRisk);
        Assert.Equal(first.MaximumAllowedRisk, second.MaximumAllowedRisk);
        Assert.Equal(first.Exposure, second.Exposure);
        Assert.Equal(first.Reason, second.Reason);
    }

    // ── non-Closed position: never risk-evaluated, never a fabricated result ───────────────────────

    [Theory]
    [InlineData(PositionStatus.NotExecutable)]
    [InlineData(PositionStatus.InvalidEntry)]
    [InlineData(PositionStatus.InsufficientFutureData)]
    [InlineData(PositionStatus.InvalidExit)]
    public void NonClosedPosition_IsNeverRiskEvaluated(PositionStatus status)
    {
        var position = new SimulatedPosition(
            1, status, "not closed", DirectionCandidate.BUY_CANDIDATE, EntryTs, null, 0,
            null, null, null, null, null, null, null);

        RiskEvaluationResult result = PositionRiskEvaluator.Evaluate(position, 5, Account(), Mes(), Policy(0.01m), Risk(2m), NoCosts());

        Assert.False(result.IsAllowed);
        Assert.Equal(0, result.AllowedQuantity);
        Assert.Equal(PositionRiskReason.PositionNotExecutable, result.Reason);
    }
}
