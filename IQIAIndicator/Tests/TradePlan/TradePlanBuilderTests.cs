using System;
using System.Collections.Generic;
using IQIAIndicator.Core;
using IQIAIndicator.Engine.Entry;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.TradePlan;

namespace IQIAIndicator.Tests.TradePlanTests;

/// <summary>
/// Sprint 15.8 (TEST A-H). Unit-level coverage of TradePlanBuilder built directly against synthetic
/// EntryTriggerCandidate/TradeRiskParameters, independent of the full pipeline - the same approach
/// DecisionDirectionCoherenceTests uses. This is deliberate: the Phase 1 audit found no Risk Engine,
/// stop-loss methodology or account risk budget anywhere in the codebase, so production
/// (IQIAIndicator.cs) always passes RiskParameters=null today and can only ever reach NO_TRADE or
/// SIGNAL_ONLY. TEST D-H exercise the risk/sizing/RR math as if a future Risk Engine had supplied
/// StopLoss/RiskPerTrade, proving the computation itself is correct for when that day comes - without
/// the production wiring fabricating a value it does not have.
/// </summary>
public static class TradePlanBuilderTests
{
    public static void RunAll()
    {
        AssertNoActionAndWatchProduceNoTrade();
        AssertBuyCandidateWithoutRiskParametersProducesSignalOnly();
        AssertSellCandidateWithoutRiskParametersProducesSignalOnly();
        AssertBuyWithFullRiskParametersProducesPlanReady();
        AssertSellWithFullRiskParametersProducesPlanReady();
        AssertZeroRiskProducesPlanBlockedWithNoNaNOrInfinity();
        AssertImpossiblePositionSizingLeavesPositionSizeUnavailable();
        AssertImpossibleRiskRewardReportsNull();
        AssertRiskRewardBelowMinimumProducesPlanRejected();
    }

    // TEST A
    private static void AssertNoActionAndWatchProduceNoTrade()
    {
        TradePlan noAction = Build(DirectionCandidate.NO_ACTION, currentPrice: 100m, estimatedEquilibrium: 110.0);
        Assert(noAction.Status == TradePlanStatus.NO_TRADE, $"NO_ACTION must produce TradePlanStatus.NO_TRADE. Actual={noAction.Status}.");
        Assert(!noAction.IsValid, "NO_TRADE must never be reported as IsValid.");
        Assert(noAction.EntryPrice is null && noAction.StopLoss is null && noAction.TakeProfit is null,
            "NO_TRADE must not carry an entry/SL/TP - there is no signal to plan against.");

        TradePlan watch = Build(DirectionCandidate.WATCH, currentPrice: 100m, estimatedEquilibrium: 110.0);
        Assert(watch.Status == TradePlanStatus.NO_TRADE, $"WATCH must also produce TradePlanStatus.NO_TRADE (not yet a directional candidate). Actual={watch.Status}.");
    }

    // TEST B
    private static void AssertBuyCandidateWithoutRiskParametersProducesSignalOnly()
    {
        TradePlan plan = Build(DirectionCandidate.BUY_CANDIDATE, currentPrice: 100m, estimatedEquilibrium: 110.0);
        Assert(plan.Status == TradePlanStatus.SIGNAL_ONLY,
            $"BUY_CANDIDATE with no StopLoss/RiskPerTrade source must produce SIGNAL_ONLY (categorical capability gap, not a fabricated plan). Actual={plan.Status}.");
        Assert(plan.EntryPrice == 100m, $"EntryPrice must still be the real CurrentPrice. Actual={plan.EntryPrice}.");
        Assert(plan.StopLoss is null, "StopLoss must stay null - no methodology exists to source it from.");
        Assert(plan.PositionSize is null, "PositionSize must stay null with no risk budget.");
        Assert(!plan.IsValid, "SIGNAL_ONLY must never be reported as IsValid.");
    }

    // TEST C
    private static void AssertSellCandidateWithoutRiskParametersProducesSignalOnly()
    {
        TradePlan plan = Build(DirectionCandidate.SELL_CANDIDATE, currentPrice: 100m, estimatedEquilibrium: 90.0);
        Assert(plan.Status == TradePlanStatus.SIGNAL_ONLY,
            $"SELL_CANDIDATE with no StopLoss/RiskPerTrade source must produce SIGNAL_ONLY. Actual={plan.Status}.");
        Assert(plan.EntryPrice == 100m, $"EntryPrice must still be the real CurrentPrice. Actual={plan.EntryPrice}.");
    }

    // TEST D
    private static void AssertBuyWithFullRiskParametersProducesPlanReady()
    {
        TradePlan plan = Build(
            DirectionCandidate.BUY_CANDIDATE,
            currentPrice: 100m,
            estimatedEquilibrium: 110.0,
            riskParameters: new TradeRiskParameters(StopLoss: 95m, RiskPerTrade: 1000m));

        Assert(plan.Status == TradePlanStatus.PLAN_READY, $"BUY with valid Entry/SL/TP/RiskPerTrade must produce PLAN_READY. Actual={plan.Status}.");
        Assert(plan.IsValid, "PLAN_READY must be reported as IsValid.");
        Assert(plan.EntryPrice == 100m && plan.StopLoss == 95m && plan.TakeProfit == 110m,
            $"Entry/SL/TP must match the inputs unchanged. Actual Entry={plan.EntryPrice}, SL={plan.StopLoss}, TP={plan.TakeProfit}.");
        Assert(plan.RiskPerUnit == 250m, $"RiskPerUnit must be abs(100-95)*PointValue(50)=250. Actual={plan.RiskPerUnit}.");
        Assert(plan.PositionSize == 4, $"PositionSize must be floor(1000/250)=4. Actual={plan.PositionSize}.");
        Assert(plan.RiskAmount == 1000m, $"RiskAmount must be RiskPerUnit(250)*PositionSize(4)=1000. Actual={plan.RiskAmount}.");
        Assert(plan.RiskRewardRatio is double rr && Math.Abs(rr - 2.0) < 1e-9,
            $"RR must be Reward(10)/Risk(5)=2.0 for a BUY. Actual={plan.RiskRewardRatio}.");
    }

    // TEST E
    private static void AssertSellWithFullRiskParametersProducesPlanReady()
    {
        TradePlan plan = Build(
            DirectionCandidate.SELL_CANDIDATE,
            currentPrice: 100m,
            estimatedEquilibrium: 90.0,
            riskParameters: new TradeRiskParameters(StopLoss: 105m, RiskPerTrade: 1000m));

        Assert(plan.Status == TradePlanStatus.PLAN_READY, $"SELL with valid Entry/SL/TP/RiskPerTrade must produce PLAN_READY. Actual={plan.Status}.");
        Assert(plan.EntryPrice == 100m && plan.StopLoss == 105m && plan.TakeProfit == 90m,
            $"Entry/SL/TP must match the inputs unchanged. Actual Entry={plan.EntryPrice}, SL={plan.StopLoss}, TP={plan.TakeProfit}.");
        Assert(plan.PositionSize == 4, $"PositionSize must be floor(1000/250)=4. Actual={plan.PositionSize}.");
        Assert(plan.RiskRewardRatio is double rr && Math.Abs(rr - 2.0) < 1e-9,
            $"RR must be Reward(10)/Risk(5)=2.0 for a SELL. Actual={plan.RiskRewardRatio}.");
    }

    // TEST F
    private static void AssertZeroRiskProducesPlanBlockedWithNoNaNOrInfinity()
    {
        TradePlan plan = Build(
            DirectionCandidate.BUY_CANDIDATE,
            currentPrice: 100m,
            estimatedEquilibrium: 110.0,
            riskParameters: new TradeRiskParameters(StopLoss: 100m, RiskPerTrade: 1000m));

        Assert(plan.Status == TradePlanStatus.PLAN_BLOCKED, $"StopLoss == EntryPrice (zero risk) must produce PLAN_BLOCKED. Actual={plan.Status}.");
        Assert(plan.RiskPerUnit == 0m, $"RiskPerUnit must be exactly 0, not NaN/Infinity. Actual={plan.RiskPerUnit}.");
        Assert(plan.PositionSize is null, "PositionSize must stay null - never sized against zero risk.");
        Assert(plan.RiskRewardRatio is null, "RiskRewardRatio must stay null - never divide by zero risk.");
        Assert(!string.IsNullOrWhiteSpace(plan.InvalidationReason), "PLAN_BLOCKED must carry an explicit InvalidationReason.");
    }

    // TEST G
    private static void AssertImpossiblePositionSizingLeavesPositionSizeUnavailable()
    {
        TradePlan noBudget = Build(
            DirectionCandidate.BUY_CANDIDATE,
            currentPrice: 100m,
            estimatedEquilibrium: 110.0,
            riskParameters: new TradeRiskParameters(StopLoss: 95m, RiskPerTrade: null));
        Assert(noBudget.PositionSize is null, "With no RiskPerTrade budget, PositionSize must stay null - never a fictional size.");
        Assert(noBudget.RiskAmount is null, "With no PositionSize, RiskAmount must also stay null.");
        Assert(noBudget.Status == TradePlanStatus.SIGNAL_ONLY, $"Missing only the risk budget (Entry/SL/TP all valid) must be SIGNAL_ONLY, not PLAN_BLOCKED. Actual={noBudget.Status}.");

        TradePlan tooSmallBudget = Build(
            DirectionCandidate.BUY_CANDIDATE,
            currentPrice: 100m,
            estimatedEquilibrium: 110.0,
            riskParameters: new TradeRiskParameters(StopLoss: 95m, RiskPerTrade: 10m));
        Assert(tooSmallBudget.PositionSize is null,
            $"A risk budget smaller than RiskPerUnit must floor to zero contracts, not a fictional 0 or negative size. Actual={tooSmallBudget.PositionSize}.");
    }

    // TEST H
    private static void AssertImpossibleRiskRewardReportsNull()
    {
        TradePlan plan = Build(
            DirectionCandidate.BUY_CANDIDATE,
            currentPrice: 100m,
            estimatedEquilibrium: null,
            riskParameters: new TradeRiskParameters(StopLoss: 95m, RiskPerTrade: 1000m));

        Assert(plan.TakeProfit is null, "With no EstimatedEquilibrium, TakeProfit must stay null.");
        Assert(plan.RiskRewardRatio is null, "Without a valid TakeProfit, RiskRewardRatio must be null (displayed as N/A), never fabricated.");
        Assert(plan.Status == TradePlanStatus.SIGNAL_ONLY, $"Missing only TakeProfit (Entry/SL/sizing all valid) must be SIGNAL_ONLY. Actual={plan.Status}.");
    }

    // TEST I (audit 2026-08-29): MinRiskReward gate.
    private static void AssertRiskRewardBelowMinimumProducesPlanRejected()
    {
        // BUY at 100, equilibrium 101 -> reward 1; StopLoss 95 -> risk 5; R:R = 0.20.
        TradePlan rejected = Build(
            DirectionCandidate.BUY_CANDIDATE,
            currentPrice: 100m,
            estimatedEquilibrium: 101.0,
            riskParameters: new TradeRiskParameters(StopLoss: 95m, RiskPerTrade: 1000m, MinRiskReward: 1.5));
        Assert(rejected.Status == TradePlanStatus.PLAN_REJECTED,
            $"R:R 0.20 below MinRiskReward 1.5 must produce PLAN_REJECTED. Actual={rejected.Status}.");
        Assert(!rejected.IsValid, "PLAN_REJECTED must never be reported as IsValid.");
        Assert(!string.IsNullOrWhiteSpace(rejected.InvalidationReason), "PLAN_REJECTED must carry an explicit InvalidationReason.");

        // Same geometry, no gate configured -> the plan is still PLAN_READY (gate is opt-in).
        TradePlan ungated = Build(
            DirectionCandidate.BUY_CANDIDATE,
            currentPrice: 100m,
            estimatedEquilibrium: 101.0,
            riskParameters: new TradeRiskParameters(StopLoss: 95m, RiskPerTrade: 1000m));
        Assert(ungated.Status == TradePlanStatus.PLAN_READY,
            $"With no MinRiskReward, the same weak-R:R plan stays PLAN_READY. Actual={ungated.Status}.");

        // R:R above the minimum is not rejected: equilibrium 120 -> reward 20, risk 5, R:R = 4.0.
        TradePlan accepted = Build(
            DirectionCandidate.BUY_CANDIDATE,
            currentPrice: 100m,
            estimatedEquilibrium: 120.0,
            riskParameters: new TradeRiskParameters(StopLoss: 95m, RiskPerTrade: 1000m, MinRiskReward: 1.5));
        Assert(accepted.Status == TradePlanStatus.PLAN_READY,
            $"R:R 4.0 above MinRiskReward 1.5 must stay PLAN_READY. Actual={accepted.Status}.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static TradePlan Build(
        DirectionCandidate direction,
        decimal currentPrice,
        double? estimatedEquilibrium,
        TradeRiskParameters? riskParameters = null)
    {
        var assessment = new EntryTriggerAssessment(
            TriggerStatus: EntryTriggerStatus.READY,
            Direction: direction,
            ScientificConfidence: 0.8,
            OpportunityPriority: 0.8,
            Reason: EntryTriggerReason.READY,
            EstimatedEquilibrium: estimatedEquilibrium,
            DistanceToEquilibrium: null,
            Timestamp: DateTime.UtcNow);

        var entryAssessment = new EntryAssessment(
            ScientificAssessment: null!,
            AssessmentQuality: 0.8,
            EntryReadiness: EntryReadiness.READY_FOR_NEXT_STAGE,
            OpportunityStatus: OpportunityStatus.QUALIFIED,
            OpportunityPriority: 0.8,
            OpportunityReasons: Array.Empty<string>(),
            BlockingIssues: Array.Empty<string>(),
            SupportingEvidence: Array.Empty<string>(),
            Warnings: Array.Empty<string>(),
            Diagnostics: Array.Empty<string>());

        var entryCandidate = new EntryCandidate(
            entryAssessment,
            DateTime.UtcNow,
            OpportunityStatus.QUALIFIED,
            OpportunityPriority: 0.8,
            OpportunityReasons: Array.Empty<string>(),
            Warnings: Array.Empty<string>(),
            Diagnostics: Array.Empty<string>());

        var entryTriggerCandidate = new EntryTriggerCandidate(
            assessment,
            entryCandidate,
            currentPrice,
            Warnings: Array.Empty<string>(),
            Diagnostics: Array.Empty<string>(),
            DateTime.UtcNow);

        var instrumentInfo = new InstrumentInfo("TEST", TickSize: 0.25m, TickValue: 12.5m, PointValue: 50m, Decimals: 2);

        var context = new TradePlanContext(entryTriggerCandidate, instrumentInfo, riskParameters);
        return new TradePlanBuilder().Build(context);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
