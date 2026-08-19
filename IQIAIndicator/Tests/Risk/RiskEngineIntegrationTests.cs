using System;
using IQIAIndicator.Core;
using IQIAIndicator.Engine.Entry;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Risk;
using TradePlanNs = IQIAIndicator.Engine.TradePlan;

namespace IQIAIndicator.Tests.RiskTests;

/// <summary>
/// Sprint 15.25 (Lot 11, Section 13). Integration coverage: TradePlan (built by the real
/// TradePlanEngine/TradePlanBuilder, never hand-crafted) -&gt; RiskEngineRequestFactory -&gt; RiskEngine ->
/// RiskAssessment. Lot 10's RiskEngineTests.cs already covers RiskEngine's own calculation core in
/// isolation; this file proves the wiring between TradePlan and that engine, and that the integration
/// introduces no capital invention, no side effects and no hardcoded ES/MES branching.
/// </summary>
public static class RiskEngineIntegrationTests
{
    public static void RunAll()
    {
        Test01_PipelineProducesRiskAssessment();
        Test02_RiskEngineRejected();
        Test03_RiskEngineAccepted();
        Test04_MissingCapitalIsRejectedNotInvented();
        Test05_EsVsMesUseDistinctSpecificationsNoHardcoding();
        Test06_NoSideEffectsOnInputs();
        Test07_TradePlanWithoutStopLossIsRejectedNotInvented();
        Test08_NoAssessableCandidateProducesNoRequest();
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────────────────────────

    private static AccountState Account(
        decimal initialCapital = 50000m,
        decimal currentEquity = 50000m,
        decimal? currentBalance = null,
        decimal peakEquity = 50000m,
        decimal dailyStartingEquity = 50000m,
        decimal dailyPnL = 0m,
        decimal riskUsedToday = 0m,
        decimal openRisk = 0m) =>
        new(initialCapital, currentEquity, currentBalance, peakEquity, dailyStartingEquity, dailyPnL, riskUsedToday, openRisk);

    private static RiskPolicy Policy(
        decimal? maxRiskPerTradePercent = 0.02m,
        decimal? maxRiskPerTradeAmount = null,
        double? minRiskReward = null) =>
        new(maxRiskPerTradePercent, maxRiskPerTradeAmount, null, null, null, null, null, null, minRiskReward, null);

    private static InstrumentRiskSpecification EsSpec() =>
        InstrumentRiskSpecification.FromInstrumentInfo(
            new InstrumentInfo("ES", TickSize: 0.25m, TickValue: 12.5m, PointValue: 50m, Decimals: 2),
            minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static InstrumentRiskSpecification MesSpec() =>
        InstrumentRiskSpecification.FromInstrumentInfo(
            new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
            minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    /// <summary>Builds a TradePlan through the real TradePlanEngine/TradePlanBuilder - never hand-crafted -
    /// mirroring TradePlanBuilderTests.cs's own fixture. riskParameters plays the role a future SL-source
    /// sprint would occupy; today's real pipeline always passes null (see IQIAIndicator.cs), which is
    /// exactly what Test07 below exercises.</summary>
    private static TradePlanNs.TradePlan BuildTradePlan(
        DirectionCandidate direction,
        decimal currentPrice,
        double? estimatedEquilibrium,
        TradePlanNs.TradeRiskParameters? riskParameters = null)
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
            entryAssessment, DateTime.UtcNow, OpportunityStatus.QUALIFIED, OpportunityPriority: 0.8,
            OpportunityReasons: Array.Empty<string>(), Warnings: Array.Empty<string>(), Diagnostics: Array.Empty<string>());

        var entryTriggerCandidate = new EntryTriggerCandidate(
            assessment, entryCandidate, currentPrice, Warnings: Array.Empty<string>(), Diagnostics: Array.Empty<string>(), DateTime.UtcNow);

        // ATAS-facing InstrumentInfo used only to build the TradePlan itself (Entry/TP math) - deliberately
        // distinct from the InstrumentRiskSpecification passed to RiskEngine, exactly like production
        // (IQIAIndicator.cs builds Core.InstrumentInfo for TradePlanContext and InstrumentRiskSpecification
        // separately for the Risk stage).
        var instrumentInfo = new InstrumentInfo("ES", TickSize: 0.25m, TickValue: 12.5m, PointValue: 50m, Decimals: 2);
        var context = new TradePlanNs.TradePlanContext(entryTriggerCandidate, instrumentInfo, riskParameters);
        return new TradePlanNs.TradePlanEngine().Process(context);
    }

    // ── TEST 1: Pipeline avec RiskAssessment ────────────────────────────────────────────────────────

    private static void Test01_PipelineProducesRiskAssessment()
    {
        TradePlanNs.TradePlan tradePlan = BuildTradePlan(
            DirectionCandidate.BUY_CANDIDATE, currentPrice: 100m, estimatedEquilibrium: 110.0,
            riskParameters: new TradePlanNs.TradeRiskParameters(StopLoss: 95m, RiskPerTrade: 1000m));

        RiskEngineRequest? request = RiskEngineRequestFactory.FromTradePlan(tradePlan, Account(), Policy(), EsSpec());
        Assert(request is not null, "A PLAN_READY-shaped TradePlan must produce a RiskEngineRequest.");

        RiskAssessment assessment = new RiskEngine().Evaluate(request!);
        Assert(assessment.Direction == TradeDirection.Buy, $"Direction must be mapped from BUY_CANDIDATE. Actual={assessment.Direction}.");
        Assert(assessment.EntryPrice == 100m, $"EntryPrice must be taken from TradePlan unchanged. Actual={assessment.EntryPrice}.");
    }

    // ── TEST 2: RiskEngine REJECTED ──────────────────────────────────────────────────────────────────

    private static void Test02_RiskEngineRejected()
    {
        // Explicitly invalid per Lot 10's own rules: SL on the wrong side of Entry for a BUY.
        TradePlanNs.TradePlan tradePlan = BuildTradePlan(
            DirectionCandidate.BUY_CANDIDATE, currentPrice: 100m, estimatedEquilibrium: 110.0,
            riskParameters: new TradePlanNs.TradeRiskParameters(StopLoss: 105m, RiskPerTrade: 1000m));

        RiskEngineRequest? request = RiskEngineRequestFactory.FromTradePlan(tradePlan, Account(), Policy(), EsSpec());
        Assert(request is not null, "A candidate with a (wrong-side) SL still produces a request - the engine, not the factory, must reject it.");

        RiskAssessment assessment = new RiskEngine().Evaluate(request!);
        Assert(assessment.Status == RiskDecisionStatus.REJECTED, $"A wrong-side SL must be REJECTED. Actual={assessment.Status}.");
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_STOP_LOSS), "Must report INVALID_STOP_LOSS.");
    }

    // ── TEST 3: RiskEngine ACCEPTED ──────────────────────────────────────────────────────────────────

    private static void Test03_RiskEngineAccepted()
    {
        TradePlanNs.TradePlan tradePlan = BuildTradePlan(
            DirectionCandidate.BUY_CANDIDATE, currentPrice: 100m, estimatedEquilibrium: 115.0,
            riskParameters: new TradePlanNs.TradeRiskParameters(StopLoss: 95m, RiskPerTrade: 1000m));

        RiskEngineRequest? request = RiskEngineRequestFactory.FromTradePlan(tradePlan, Account(), Policy(), EsSpec());
        RiskAssessment assessment = new RiskEngine().Evaluate(request!);

        Assert(assessment.Status == RiskDecisionStatus.ACCEPTED, $"A fully valid candidate must be ACCEPTED. Reasons=[{string.Join(",", assessment.RejectionReasons)}].");
        Assert(assessment.PositionSize is not null, "An ACCEPTED assessment must carry a resolved PositionSize.");
    }

    // ── TEST 4: Capital absent ───────────────────────────────────────────────────────────────────────

    private static void Test04_MissingCapitalIsRejectedNotInvented()
    {
        TradePlanNs.TradePlan tradePlan = BuildTradePlan(
            DirectionCandidate.BUY_CANDIDATE, currentPrice: 100m, estimatedEquilibrium: 110.0,
            riskParameters: new TradePlanNs.TradeRiskParameters(StopLoss: 95m, RiskPerTrade: 1000m));

        // Mirrors IQIAIndicator.cs's RiskInitialCapital/RiskCurrentEquity default of 0 - "not configured yet".
        RiskEngineRequest? request = RiskEngineRequestFactory.FromTradePlan(
            tradePlan, Account(initialCapital: 0m, currentEquity: 0m), Policy(), EsSpec());
        RiskAssessment assessment = new RiskEngine().Evaluate(request!);

        Assert(assessment.Status == RiskDecisionStatus.REJECTED, "Missing capital must be REJECTED, never silently accepted.");
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_CAPITAL), "Must report INVALID_CAPITAL.");
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_EQUITY), "Must report INVALID_EQUITY.");
        Assert(assessment.PositionSize is null, "PositionSize must stay null - never a silently-sized trade against absent capital.");
    }

    // ── TEST 5: ES vs MES ────────────────────────────────────────────────────────────────────────────

    private static void Test05_EsVsMesUseDistinctSpecificationsNoHardcoding()
    {
        TradePlanNs.TradePlan tradePlan = BuildTradePlan(
            DirectionCandidate.BUY_CANDIDATE, currentPrice: 100m, estimatedEquilibrium: 110.0,
            riskParameters: new TradePlanNs.TradeRiskParameters(StopLoss: 95m, RiskPerTrade: 1000m)); // 5pt stop

        RiskAssessment esAssessment = new RiskEngine().Evaluate(RiskEngineRequestFactory.FromTradePlan(tradePlan, Account(), Policy(), EsSpec())!);
        RiskAssessment mesAssessment = new RiskEngine().Evaluate(RiskEngineRequestFactory.FromTradePlan(tradePlan, Account(), Policy(), MesSpec())!);

        Assert(esAssessment.RiskPerUnit == 250m, $"ES: RiskPerUnit must be 5 x 50 = 250. Actual={esAssessment.RiskPerUnit}.");
        Assert(mesAssessment.RiskPerUnit == 25m, $"MES: RiskPerUnit must be 5 x 5 = 25. Actual={mesAssessment.RiskPerUnit}.");
        Assert(esAssessment.RiskPerUnit != mesAssessment.RiskPerUnit,
            "The exact same TradePlan through the exact same RiskEngine must yield different results for ES vs MES - the difference comes only from InstrumentRiskSpecification, never a symbol-keyed branch.");
    }

    // ── TEST 6: Aucun effet de bord ──────────────────────────────────────────────────────────────────

    private static void Test06_NoSideEffectsOnInputs()
    {
        TradePlanNs.TradePlan tradePlan = BuildTradePlan(
            DirectionCandidate.BUY_CANDIDATE, currentPrice: 100m, estimatedEquilibrium: 115.0,
            riskParameters: new TradePlanNs.TradeRiskParameters(StopLoss: 95m, RiskPerTrade: 1000m));
        AccountState account = Account();
        RiskPolicy policy = Policy();
        InstrumentRiskSpecification instrument = EsSpec();

        TradePlanNs.TradePlan tradePlanSnapshot = tradePlan with { };
        AccountState accountSnapshot = account with { };
        RiskPolicy policySnapshot = policy with { };
        InstrumentRiskSpecification instrumentSnapshot = instrument with { };

        RiskEngineRequest? request = RiskEngineRequestFactory.FromTradePlan(tradePlan, account, policy, instrument);
        _ = new RiskEngine().Evaluate(request!);

        Assert(tradePlanSnapshot == tradePlan, "TradePlan must not be mutated by the Risk Engine integration.");
        Assert(accountSnapshot == account, "AccountState must not be mutated by the Risk Engine integration.");
        Assert(policySnapshot == policy, "RiskPolicy must not be mutated by the Risk Engine integration.");
        Assert(instrumentSnapshot == instrument, "InstrumentRiskSpecification must not be mutated by the Risk Engine integration.");
    }

    // ── TEST 7: TradePlan sans SL (réalité de production actuelle) ─────────────────────────────────────

    private static void Test07_TradePlanWithoutStopLossIsRejectedNotInvented()
    {
        // riskParameters: null is exactly what IQIAIndicator.cs passes into TradePlanContext today (no
        // SL methodology exists - Lot 10/11 explicitly forbid inventing one here).
        TradePlanNs.TradePlan tradePlan = BuildTradePlan(DirectionCandidate.BUY_CANDIDATE, currentPrice: 100m, estimatedEquilibrium: 110.0);
        Assert(tradePlan.Status == TradePlanNs.TradePlanStatus.SIGNAL_ONLY, $"Sanity check: today's real production TradePlan (no SL source) must be SIGNAL_ONLY. Actual={tradePlan.Status}.");
        Assert(tradePlan.StopLoss is null, "Sanity check: TradePlan.StopLoss must be null with no RiskParameters source.");

        RiskEngineRequest? request = RiskEngineRequestFactory.FromTradePlan(tradePlan, Account(), Policy(), EsSpec());
        Assert(request is not null, "A BUY_CANDIDATE with a real EntryPrice still produces a request even with no StopLoss - the engine rejects it, the factory does not silently drop it.");

        RiskAssessment assessment = new RiskEngine().Evaluate(request!);
        Assert(assessment.Status == RiskDecisionStatus.REJECTED, "A TradePlan with no StopLoss must be REJECTED.");
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_STOP_LOSS), "Must report INVALID_STOP_LOSS - never an invented distance/ATR/percentage stop.");
        Assert(assessment.PositionSize is null, "PositionSize must stay null with no valid StopLoss.");
    }

    // ── TEST 8: Aucun candidat évaluable -> pas de requête ──────────────────────────────────────────

    private static void Test08_NoAssessableCandidateProducesNoRequest()
    {
        TradePlanNs.TradePlan noActionPlan = BuildTradePlan(DirectionCandidate.NO_ACTION, currentPrice: 100m, estimatedEquilibrium: 110.0);
        Assert(RiskEngineRequestFactory.FromTradePlan(noActionPlan, Account(), Policy(), EsSpec()) is null,
            "NO_TRADE (NO_ACTION direction) must never reach RiskEngine - there is nothing to assess.");

        TradePlanNs.TradePlan watchPlan = BuildTradePlan(DirectionCandidate.WATCH, currentPrice: 100m, estimatedEquilibrium: 110.0);
        Assert(RiskEngineRequestFactory.FromTradePlan(watchPlan, Account(), Policy(), EsSpec()) is null,
            "NO_TRADE (WATCH direction) must never reach RiskEngine either.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
