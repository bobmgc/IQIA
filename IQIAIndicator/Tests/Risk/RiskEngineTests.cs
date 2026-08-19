using System;
using System.Linq;
using IQIAIndicator.Engine.Risk;

namespace IQIAIndicator.Tests.RiskTests;

/// <summary>
/// Sprint 15.25 (Lot 10, Section 15). Unit-level coverage of RiskEngine, built directly against
/// synthetic AccountState/RiskPolicy/InstrumentRiskSpecification/RiskEngineRequest fixtures - the same
/// approach TradePlanBuilderTests uses for TradePlanBuilder. ES/MES values are explicit fixture literals
/// (never hidden constants inside the engine), per Lot 10 Section 15's explicit requirement.
/// Test numbering mirrors the 35-item list in the Lot 10 specification, Section 15.
/// </summary>
public static class RiskEngineTests
{
    public static void RunAll()
    {
        // Capital
        Test01_ValidCapitalDoesNotReject();
        Test02_ZeroCapitalIsRejected();
        Test03_NegativeCapitalIsRejected();
        Test04_ValidEquityDoesNotReject();
        Test05_InvalidEquityIsRejectedWithoutSpuriousBudgetReason();

        // Risk budget
        Test06_RiskBudgetIsComputedCorrectly();
        Test07_UndefinedBudgetIsRejected();
        Test08_BudgetInsufficientForTradeIsRejected();
        Test09_MaxDrawdownReachedZeroesBudget();
        Test10_DailyLossLimitReachedRejectsWithoutSpuriousBudgetReason();

        // BUY
        Test11_BuyStopLossBelowEntryIsValid();
        Test12_BuyStopLossAboveEntryIsInvalid();
        Test13_BuyTakeProfitAboveEntryIsValid();
        Test14_BuyTakeProfitBelowEntryIsInvalid();

        // SELL
        Test15_SellStopLossAboveEntryIsValid();
        Test16_SellStopLossBelowEntryIsInvalid();
        Test17_SellTakeProfitBelowEntryIsValid();
        Test18_SellTakeProfitAboveEntryIsInvalid();

        // Position sizing
        Test19_SizingIsCorrect();
        Test20_RoundingIsConservative();
        Test21_QuantityStepIsRespected();
        Test22_MaxQuantityClampsWithoutRejecting();
        Test23_ZeroResultingSizeIsRejected();

        // Risk/Reward
        Test24_ValidRiskRewardIsAccepted();
        Test25_InsufficientRiskRewardIsRejected();
        Test26_UnverifiableRiskRewardIsRejected();

        // Instrument
        Test27_EsSpecificationProducesEsRiskPerUnit();
        Test28_MesSpecificationProducesDistinctRiskPerUnit();
        Test29_InvalidInstrumentSpecIsRejected();
        Test30_InvalidTickSizeIsRejected();
        Test31_InvalidTickValueIsRejected();

        // Safety
        Test32_MultipleSimultaneousViolationsAreAllReported();
        Test33_ZeroAllowedRiskIsRejected();
        Test34_ExcessiveOpenRiskIsRejected();
        Test35_TradeIsAcceptedWhenEveryConstraintIsSatisfied();
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
        decimal? maxDailyLossPercent = null,
        decimal? maxDailyLossAmount = null,
        decimal? maxDrawdownPercent = null,
        decimal? maxDrawdownAmount = null,
        decimal? maxOpenRiskPercent = null,
        decimal? maxOpenRiskAmount = null,
        double? minRiskReward = null,
        int? maxPositionSize = null,
        int? minPositionSize = null) =>
        new(maxRiskPerTradePercent, maxRiskPerTradeAmount, maxDailyLossPercent, maxDailyLossAmount,
            maxDrawdownPercent, maxDrawdownAmount, maxOpenRiskPercent, maxOpenRiskAmount,
            minRiskReward, maxPositionSize, minPositionSize);

    // Explicit ES fixture values (Dec 2025 CME ES contract): 0.25 tick, $12.50/tick, $50/point.
    private static InstrumentRiskSpecification EsSpec(int minQuantity = 1, int maxQuantity = 50, int quantityStep = 1) =>
        new("ES", TickSize: 0.25m, TickValue: 12.5m, PointValue: 50m, minQuantity, maxQuantity, quantityStep);

    // Explicit MES fixture values (Micro E-mini): 0.25 tick, $1.25/tick, $5/point - one tenth of ES.
    private static InstrumentRiskSpecification MesSpec(int minQuantity = 1, int maxQuantity = 50, int quantityStep = 1) =>
        new("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, minQuantity, maxQuantity, quantityStep);

    private static RiskEngineRequest Request(
        TradeDirection direction,
        decimal entryPrice,
        decimal? stopLoss,
        decimal? takeProfit = null,
        InstrumentRiskSpecification? instrument = null,
        AccountState? account = null,
        RiskPolicy? policy = null) =>
        new(direction, entryPrice, stopLoss, takeProfit, instrument ?? EsSpec(), account ?? Account(), policy ?? Policy());

    private static RiskAssessment Evaluate(RiskEngineRequest request) => new RiskEngine().Evaluate(request);

    // ── Capital ──────────────────────────────────────────────────────────────────────────────────

    // TEST 01: capital valide
    private static void Test01_ValidCapitalDoesNotReject()
    {
        var assessment = Evaluate(Request(TradeDirection.Buy, 100m, 95m, 110m, account: Account(initialCapital: 50000m)));
        Assert(!assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_CAPITAL),
            "A positive InitialCapital must never produce INVALID_CAPITAL.");
        Assert(assessment.Status == RiskDecisionStatus.ACCEPTED, $"A fully valid request with valid capital must be ACCEPTED. Actual={assessment.Status}, Reasons=[{string.Join(",", assessment.RejectionReasons)}].");
    }

    // TEST 02: capital nul
    private static void Test02_ZeroCapitalIsRejected()
    {
        var assessment = Evaluate(Request(TradeDirection.Buy, 100m, 95m, 110m, account: Account(initialCapital: 0m)));
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_CAPITAL), "Zero InitialCapital must produce INVALID_CAPITAL.");
        Assert(assessment.Status == RiskDecisionStatus.REJECTED, "Zero InitialCapital must be REJECTED.");
    }

    // TEST 03: capital négatif
    private static void Test03_NegativeCapitalIsRejected()
    {
        var assessment = Evaluate(Request(TradeDirection.Buy, 100m, 95m, 110m, account: Account(initialCapital: -1000m)));
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_CAPITAL), "Negative InitialCapital must produce INVALID_CAPITAL.");
        Assert(assessment.Status == RiskDecisionStatus.REJECTED, "Negative InitialCapital must be REJECTED.");
    }

    // TEST 04: equity valide
    private static void Test04_ValidEquityDoesNotReject()
    {
        var assessment = Evaluate(Request(TradeDirection.Buy, 100m, 95m, 110m, account: Account(currentEquity: 50000m)));
        Assert(!assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_EQUITY), "A positive CurrentEquity must never produce INVALID_EQUITY.");
    }

    // TEST 05: equity invalide
    private static void Test05_InvalidEquityIsRejectedWithoutSpuriousBudgetReason()
    {
        var assessment = Evaluate(Request(TradeDirection.Buy, 100m, 95m, 110m, account: Account(currentEquity: 0m)));
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_EQUITY), "Zero CurrentEquity must produce INVALID_EQUITY.");
        Assert(!assessment.RejectionReasons.Contains(RiskRejectionReason.RISK_BUDGET_EXCEEDED),
            "RISK_BUDGET_EXCEEDED must not pile on top of INVALID_EQUITY - the equity failure alone already explains the absence of a budget.");
        Assert(assessment.Status == RiskDecisionStatus.REJECTED, "Invalid equity must be REJECTED.");
    }

    // ── Risk budget ──────────────────────────────────────────────────────────────────────────────

    // TEST 06: calcul correct du budget
    private static void Test06_RiskBudgetIsComputedCorrectly()
    {
        var assessment = Evaluate(Request(
            TradeDirection.Buy, 100m, 95m, 110m,
            account: Account(currentEquity: 50000m),
            policy: Policy(maxRiskPerTradePercent: 0.02m)));
        Assert(assessment.RiskBudget == 1000m, $"RiskBudget must be Equity(50000) x MaxRiskPerTradePercent(0.02) = 1000. Actual={assessment.RiskBudget}.");
    }

    // TEST 07: budget nul
    private static void Test07_UndefinedBudgetIsRejected()
    {
        var assessment = Evaluate(Request(
            TradeDirection.Buy, 100m, 95m, 110m,
            policy: Policy(maxRiskPerTradePercent: null, maxRiskPerTradeAmount: null)));
        Assert(assessment.RiskBudget is null, $"With no MaxRiskPerTradePercent/Amount configured, RiskBudget must stay undefined. Actual={assessment.RiskBudget}.");
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.RISK_BUDGET_EXCEEDED), "An undefined RiskBudget must produce RISK_BUDGET_EXCEEDED.");
        Assert(assessment.PositionSize is null, "PositionSize must stay null with no risk budget.");
        Assert(assessment.Status == RiskDecisionStatus.REJECTED, "An undefined risk budget must be REJECTED.");
    }

    // TEST 08: budget dépassé (le besoin du trade dépasse le budget restant après contraintes)
    private static void Test08_BudgetInsufficientForTradeIsRejected()
    {
        var assessment = Evaluate(Request(
            TradeDirection.Buy, 100m, 95m, 110m, // RiskPerUnit = 5 x 50 = 250
            account: Account(currentEquity: 50000m, dailyStartingEquity: 50000m, dailyPnL: -40m),
            policy: Policy(maxRiskPerTradePercent: 0.10m, maxDailyLossAmount: 50m)));
        Assert(assessment.RiskBudget == 10m, $"RiskBudget must be capped to the remaining daily-loss room (50-40=10), not the 10% per-trade cap (5000). Actual={assessment.RiskBudget}.");
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.POSITION_SIZE_INVALID),
            "A RiskBudget (10) smaller than RiskPerUnit (250) must produce POSITION_SIZE_INVALID.");
        Assert(!assessment.RejectionReasons.Contains(RiskRejectionReason.DAILY_LOSS_LIMIT), "The daily loss limit itself was not breached (10 > 0) - only reported as budget-insufficient-for-sizing.");
        Assert(assessment.Status == RiskDecisionStatus.REJECTED, "Insufficient budget for even one contract must be REJECTED.");
    }

    // TEST 09: drawdown maximum atteint
    private static void Test09_MaxDrawdownReachedZeroesBudget()
    {
        var assessment = Evaluate(Request(
            TradeDirection.Buy, 100m, 95m, 110m,
            account: Account(currentEquity: 44000m, peakEquity: 50000m),
            policy: Policy(maxRiskPerTradePercent: 0.02m, maxDrawdownPercent: 0.10m)));
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.MAX_DRAWDOWN_REACHED),
            $"CurrentDrawdownPercent (6000/50000=0.12) >= MaxDrawdownPercent (0.10) must produce MAX_DRAWDOWN_REACHED.");
        Assert(assessment.RiskBudget == 0m, $"RiskBudget must be forced to zero once max drawdown is reached. Actual={assessment.RiskBudget}.");
        Assert(assessment.PositionSize is null, "PositionSize must stay null once max drawdown is reached.");
        Assert(assessment.Status == RiskDecisionStatus.REJECTED, "Max drawdown reached must be REJECTED.");
    }

    // TEST 10: daily loss maximum atteint
    private static void Test10_DailyLossLimitReachedRejectsWithoutSpuriousBudgetReason()
    {
        var assessment = Evaluate(Request(
            TradeDirection.Buy, 100m, 95m, 110m,
            account: Account(currentEquity: 50000m, dailyStartingEquity: 50000m, dailyPnL: -1200m),
            policy: Policy(maxRiskPerTradePercent: 0.02m, maxDailyLossAmount: 1000m)));
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.DAILY_LOSS_LIMIT),
            "DailyPnL (-1200) exceeding MaxDailyLossAmount (1000) must produce DAILY_LOSS_LIMIT.");
        Assert(!assessment.RejectionReasons.Contains(RiskRejectionReason.RISK_BUDGET_EXCEEDED),
            "RISK_BUDGET_EXCEEDED must not pile on top of DAILY_LOSS_LIMIT - the daily-loss failure alone already explains the exhausted budget.");
        Assert(assessment.Status == RiskDecisionStatus.REJECTED, "Daily loss limit reached must be REJECTED.");
    }

    // ── BUY ──────────────────────────────────────────────────────────────────────────────────────

    // TEST 11: SL correctement placé sous Entry
    private static void Test11_BuyStopLossBelowEntryIsValid()
    {
        var assessment = Evaluate(Request(TradeDirection.Buy, 100m, 95m));
        Assert(!assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_STOP_LOSS), "SL below Entry for a BUY must be valid.");
        Assert(assessment.RiskDistance == 5m, $"RiskDistance must be Entry(100)-SL(95)=5. Actual={assessment.RiskDistance}.");
    }

    // TEST 12: SL incorrect
    private static void Test12_BuyStopLossAboveEntryIsInvalid()
    {
        var assessment = Evaluate(Request(TradeDirection.Buy, 100m, 105m));
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_STOP_LOSS), "SL above Entry for a BUY must be INVALID_STOP_LOSS.");
        Assert(assessment.RiskDistance is null, "RiskDistance must stay null when the SL is on the wrong side.");
        Assert(assessment.Status == RiskDecisionStatus.REJECTED, "An invalid SL must be REJECTED.");
    }

    // TEST 13: TP correctement placé au-dessus Entry
    private static void Test13_BuyTakeProfitAboveEntryIsValid()
    {
        var assessment = Evaluate(Request(TradeDirection.Buy, 100m, 95m, 110m));
        Assert(!assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_TAKE_PROFIT), "TP above Entry for a BUY must be valid.");
        Assert(assessment.RewardDistance == 10m, $"RewardDistance must be TP(110)-Entry(100)=10. Actual={assessment.RewardDistance}.");
    }

    // TEST 14: TP incorrect
    private static void Test14_BuyTakeProfitBelowEntryIsInvalid()
    {
        var assessment = Evaluate(Request(TradeDirection.Buy, 100m, 95m, 90m));
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_TAKE_PROFIT), "TP below Entry for a BUY must be INVALID_TAKE_PROFIT.");
        Assert(assessment.RewardDistance is null, "RewardDistance must stay null when the TP is on the wrong side.");
        Assert(assessment.Status == RiskDecisionStatus.REJECTED, "An invalid TP must be REJECTED.");
    }

    // ── SELL ─────────────────────────────────────────────────────────────────────────────────────

    // TEST 15: SL correctement placé au-dessus Entry
    private static void Test15_SellStopLossAboveEntryIsValid()
    {
        var assessment = Evaluate(Request(TradeDirection.Sell, 100m, 105m));
        Assert(!assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_STOP_LOSS), "SL above Entry for a SELL must be valid.");
        Assert(assessment.RiskDistance == 5m, $"RiskDistance must be SL(105)-Entry(100)=5. Actual={assessment.RiskDistance}.");
    }

    // TEST 16: SL incorrect
    private static void Test16_SellStopLossBelowEntryIsInvalid()
    {
        var assessment = Evaluate(Request(TradeDirection.Sell, 100m, 95m));
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_STOP_LOSS), "SL below Entry for a SELL must be INVALID_STOP_LOSS.");
        Assert(assessment.Status == RiskDecisionStatus.REJECTED, "An invalid SL must be REJECTED.");
    }

    // TEST 17: TP correctement placé sous Entry
    private static void Test17_SellTakeProfitBelowEntryIsValid()
    {
        var assessment = Evaluate(Request(TradeDirection.Sell, 100m, 105m, 90m));
        Assert(!assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_TAKE_PROFIT), "TP below Entry for a SELL must be valid.");
        Assert(assessment.RewardDistance == 10m, $"RewardDistance must be Entry(100)-TP(90)=10. Actual={assessment.RewardDistance}.");
    }

    // TEST 18: TP incorrect
    private static void Test18_SellTakeProfitAboveEntryIsInvalid()
    {
        var assessment = Evaluate(Request(TradeDirection.Sell, 100m, 105m, 110m));
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_TAKE_PROFIT), "TP above Entry for a SELL must be INVALID_TAKE_PROFIT.");
        Assert(assessment.Status == RiskDecisionStatus.REJECTED, "An invalid TP must be REJECTED.");
    }

    // ── Position sizing ──────────────────────────────────────────────────────────────────────────

    // TEST 19: sizing correct
    private static void Test19_SizingIsCorrect()
    {
        var assessment = Evaluate(Request(
            TradeDirection.Buy, 100m, 95m, 110m,
            account: Account(currentEquity: 50000m),
            policy: Policy(maxRiskPerTradePercent: 0.02m))); // budget=1000, RiskPerUnit=250
        Assert(assessment.PositionSize == 4, $"PositionSize must be floor(1000/250)=4. Actual={assessment.PositionSize}.");
        Assert(assessment.RiskAmount == 1000m, $"RiskAmount must be RiskPerUnit(250)*PositionSize(4)=1000. Actual={assessment.RiskAmount}.");
        Assert(assessment.Status == RiskDecisionStatus.ACCEPTED, "A correctly sized trade must be ACCEPTED.");
    }

    // TEST 20: rounding conservateur (2.8 contrats -> 2, jamais 3)
    private static void Test20_RoundingIsConservative()
    {
        var assessment = Evaluate(Request(
            TradeDirection.Buy, 100m, 98m, 110m, // RiskDistance=2, RiskPerUnit=2*50=100
            account: Account(currentEquity: 14000m),
            policy: Policy(maxRiskPerTradePercent: 0.02m))); // budget = 280 -> 280/100 = 2.8
        Assert(assessment.RiskBudget == 280m, $"RiskBudget must be 14000*0.02=280. Actual={assessment.RiskBudget}.");
        Assert(assessment.PositionSize == 2, $"2.8 contracts must floor to 2, never 3. Actual={assessment.PositionSize}.");
        Assert(assessment.PositionSize != 3, "Conservative rounding must never round up to 3.");
    }

    // TEST 21: quantity step
    private static void Test21_QuantityStepIsRespected()
    {
        var assessment = Evaluate(Request(
            TradeDirection.Buy, 100m, 98m, 110m, // RiskDistance=2, RiskPerUnit=100
            instrument: EsSpec(minQuantity: 1, maxQuantity: 50, quantityStep: 5),
            account: Account(currentEquity: 60000m),
            policy: Policy(maxRiskPerTradePercent: 0.02m))); // budget=1200 -> raw=12 -> step 5 -> 10
        Assert(assessment.PositionSize == 10, $"raw=floor(1200/100)=12 must round down to the nearest QuantityStep(5)=10. Actual={assessment.PositionSize}.");
    }

    // TEST 22: max quantity
    private static void Test22_MaxQuantityClampsWithoutRejecting()
    {
        var assessment = Evaluate(Request(
            TradeDirection.Buy, 100m, 99.8m, 110m, // RiskDistance=0.2, RiskPerUnit=0.2*50=10
            instrument: EsSpec(minQuantity: 1, maxQuantity: 50, quantityStep: 1),
            account: Account(currentEquity: 50000m),
            policy: Policy(maxRiskPerTradePercent: 0.02m))); // budget=1000 -> raw=100 -> clamp to MaxQuantity=50
        Assert(assessment.PositionSize == 50, $"raw=floor(1000/10)=100 must clamp down to MaxQuantity=50. Actual={assessment.PositionSize}.");
        Assert(assessment.Status == RiskDecisionStatus.ACCEPTED, "Clamping down to MaxQuantity is a safety cap, not a rejection.");
        Assert(assessment.Diagnostics.Any(d => d.Contains("clamped", StringComparison.OrdinalIgnoreCase)), "Clamping must be recorded in Diagnostics for observability.");
    }

    // TEST 23: sizing résultant à zéro
    private static void Test23_ZeroResultingSizeIsRejected()
    {
        var assessment = Evaluate(Request(
            TradeDirection.Buy, 100m, 95m, 110m, // RiskPerUnit=250
            account: Account(currentEquity: 5000m),
            policy: Policy(maxRiskPerTradePercent: 0.02m))); // budget=100 -> floor(100/250)=0
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.POSITION_SIZE_INVALID), "A budget smaller than RiskPerUnit must produce POSITION_SIZE_INVALID.");
        Assert(assessment.PositionSize is null, "PositionSize must stay null - never a fictional zero-or-more size.");
        Assert(assessment.RiskAmount is null, "RiskAmount must stay null with no PositionSize.");
        Assert(assessment.Status == RiskDecisionStatus.REJECTED, "A zero resulting size must be REJECTED.");
    }

    // ── Risk/Reward ──────────────────────────────────────────────────────────────────────────────

    // TEST 24: R:R valide
    private static void Test24_ValidRiskRewardIsAccepted()
    {
        var assessment = Evaluate(Request(
            TradeDirection.Buy, 100m, 95m, 115m, // risk=5, reward=15 -> RR=3.0
            policy: Policy(maxRiskPerTradePercent: 0.02m, minRiskReward: 2.0)));
        Assert(assessment.RiskRewardRatio is double rr && Math.Abs(rr - 3.0) < 1e-9, $"RR must be Reward(15)/Risk(5)=3.0. Actual={assessment.RiskRewardRatio}.");
        Assert(!assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_RISK_REWARD), "RR (3.0) >= MinRiskReward (2.0) must not be rejected.");
        Assert(assessment.Status == RiskDecisionStatus.ACCEPTED, "A valid RR above the minimum must be ACCEPTED.");
    }

    // TEST 25: R:R insuffisant
    private static void Test25_InsufficientRiskRewardIsRejected()
    {
        var assessment = Evaluate(Request(
            TradeDirection.Buy, 100m, 95m, 105m, // risk=5, reward=5 -> RR=1.0
            policy: Policy(maxRiskPerTradePercent: 0.02m, minRiskReward: 2.0)));
        Assert(assessment.RiskRewardRatio is double rr && Math.Abs(rr - 1.0) < 1e-9, $"RR must be Reward(5)/Risk(5)=1.0. Actual={assessment.RiskRewardRatio}.");
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_RISK_REWARD), "RR (1.0) < MinRiskReward (2.0) must produce INVALID_RISK_REWARD.");
        Assert(assessment.Status == RiskDecisionStatus.REJECTED, "An insufficient RR must be REJECTED.");
    }

    // TEST 26: R:R invalide (aucun TP fourni alors qu'un minimum est exigé - impossible à vérifier)
    private static void Test26_UnverifiableRiskRewardIsRejected()
    {
        var assessment = Evaluate(Request(
            TradeDirection.Buy, 100m, 95m, takeProfit: null,
            policy: Policy(maxRiskPerTradePercent: 0.02m, minRiskReward: 2.0)));
        Assert(assessment.RiskRewardRatio is null, "RiskRewardRatio must stay null with no TakeProfit.");
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_TAKE_PROFIT),
            "A configured MinRiskReward with no TakeProfit to verify it against must be rejected as INVALID_TAKE_PROFIT.");
        Assert(assessment.Status == RiskDecisionStatus.REJECTED, "An unverifiable RR must be REJECTED.");
    }

    // ── Instrument ───────────────────────────────────────────────────────────────────────────────

    // TEST 27: ES specification
    private static void Test27_EsSpecificationProducesEsRiskPerUnit()
    {
        var assessment = Evaluate(Request(TradeDirection.Buy, 100m, 95m, instrument: EsSpec())); // 5pts x $50/pt
        Assert(assessment.RiskPerUnit == 250m, $"ES: RiskPerUnit must be RiskDistance(5) x PointValue(50) = 250. Actual={assessment.RiskPerUnit}.");
    }

    // TEST 28: MES specification
    private static void Test28_MesSpecificationProducesDistinctRiskPerUnit()
    {
        var assessment = Evaluate(Request(TradeDirection.Buy, 100m, 95m, instrument: MesSpec())); // 5pts x $5/pt
        Assert(assessment.RiskPerUnit == 25m, $"MES: RiskPerUnit must be RiskDistance(5) x PointValue(5) = 25. Actual={assessment.RiskPerUnit}.");
        Assert(assessment.RiskPerUnit != 250m, "MES must never be treated as ES - the engine must not hardcode a shared PointValue.");
    }

    // TEST 29: instrument invalide
    private static void Test29_InvalidInstrumentSpecIsRejected()
    {
        var assessment = Evaluate(Request(TradeDirection.Buy, 100m, 95m, 110m, instrument: EsSpec(minQuantity: 10, maxQuantity: 5)));
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INSTRUMENT_SPEC_INVALID), "MaxQuantity < MinQuantity must produce INSTRUMENT_SPEC_INVALID.");
        Assert(assessment.Status == RiskDecisionStatus.REJECTED, "An invalid instrument spec must be REJECTED.");
    }

    // TEST 30: TickSize invalide
    private static void Test30_InvalidTickSizeIsRejected()
    {
        var assessment = Evaluate(Request(TradeDirection.Buy, 100m, 95m, 110m,
            instrument: new InstrumentRiskSpecification("ES", TickSize: 0m, TickValue: 12.5m, PointValue: 50m, MinQuantity: 1, MaxQuantity: 50, QuantityStep: 1)));
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INSTRUMENT_SPEC_INVALID), "A zero TickSize must produce INSTRUMENT_SPEC_INVALID.");
    }

    // TEST 31: TickValue invalide
    private static void Test31_InvalidTickValueIsRejected()
    {
        var assessment = Evaluate(Request(TradeDirection.Buy, 100m, 95m, 110m,
            instrument: new InstrumentRiskSpecification("ES", TickSize: 0.25m, TickValue: -1m, PointValue: 50m, MinQuantity: 1, MaxQuantity: 50, QuantityStep: 1)));
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INSTRUMENT_SPEC_INVALID), "A negative TickValue must produce INSTRUMENT_SPEC_INVALID.");
    }

    // ── Safety ───────────────────────────────────────────────────────────────────────────────────

    // TEST 32: plusieurs contraintes simultanément violées
    private static void Test32_MultipleSimultaneousViolationsAreAllReported()
    {
        var assessment = Evaluate(Request(
            TradeDirection.Buy, 100m, 105m, // wrong-side SL for a BUY
            account: Account(initialCapital: 0m, currentEquity: 44000m, peakEquity: 50000m),
            policy: Policy(maxRiskPerTradePercent: 0.02m, maxDrawdownPercent: 0.10m)));
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_CAPITAL), "Must report INVALID_CAPITAL.");
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_STOP_LOSS), "Must report INVALID_STOP_LOSS.");
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.MAX_DRAWDOWN_REACHED), "Must report MAX_DRAWDOWN_REACHED.");
        Assert(assessment.RejectionReasons.Count >= 3, $"All simultaneous violations must be reported together, not just the first one. Actual count={assessment.RejectionReasons.Count}.");
        Assert(assessment.Status == RiskDecisionStatus.REJECTED, "Multiple violations must be REJECTED.");
    }

    // TEST 33: aucun risque autorisé
    private static void Test33_ZeroAllowedRiskIsRejected()
    {
        var assessment = Evaluate(Request(TradeDirection.Buy, 100m, 95m, 110m, policy: Policy(maxRiskPerTradePercent: 0m)));
        Assert(assessment.RiskBudget == 0m, $"An explicit 0% risk policy must resolve RiskBudget to exactly 0. Actual={assessment.RiskBudget}.");
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.RISK_BUDGET_EXCEEDED), "A zero risk budget must produce RISK_BUDGET_EXCEEDED.");
        Assert(assessment.Status == RiskDecisionStatus.REJECTED, "No allowed risk must be REJECTED.");
    }

    // TEST 34: risque ouvert trop important
    private static void Test34_ExcessiveOpenRiskIsRejected()
    {
        var assessment = Evaluate(Request(
            TradeDirection.Buy, 100m, 95m, 110m,
            account: Account(openRisk: 1000m),
            policy: Policy(maxRiskPerTradePercent: 0.02m, maxOpenRiskAmount: 800m)));
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.OPEN_RISK_LIMIT), $"OpenRisk (1000) already exceeding MaxOpenRiskAmount (800) must produce OPEN_RISK_LIMIT.");
        Assert(!assessment.RejectionReasons.Contains(RiskRejectionReason.RISK_BUDGET_EXCEEDED),
            "RISK_BUDGET_EXCEEDED must not pile on top of OPEN_RISK_LIMIT - the open-risk failure alone already explains the exhausted budget.");
        Assert(assessment.Status == RiskDecisionStatus.REJECTED, "Excessive open risk must be REJECTED.");
    }

    // TEST 35: trade accepté lorsque toutes les contraintes sont satisfaites
    private static void Test35_TradeIsAcceptedWhenEveryConstraintIsSatisfied()
    {
        var assessment = Evaluate(Request(
            TradeDirection.Buy, 100m, 95m, 115m, // risk=5 (RiskPerUnit=250), reward=15 -> RR=3.0
            account: Account(currentEquity: 50000m, peakEquity: 50000m, dailyStartingEquity: 50000m, dailyPnL: 0m, openRisk: 0m),
            policy: Policy(
                maxRiskPerTradePercent: 0.02m,
                maxDailyLossAmount: 2000m,
                maxDrawdownPercent: 0.20m,
                maxOpenRiskAmount: 5000m,
                minRiskReward: 1.5,
                maxPositionSize: 50)));

        Assert(assessment.Status == RiskDecisionStatus.ACCEPTED, $"Every constraint being comfortably satisfied must be ACCEPTED. Reasons=[{string.Join(",", assessment.RejectionReasons)}].");
        Assert(assessment.RejectionReasons.Count == 0, "An accepted trade must carry zero rejection reasons.");
        Assert(assessment.PositionSize == 4, $"PositionSize must be floor(1000/250)=4. Actual={assessment.PositionSize}.");
        Assert(assessment.RiskAmount == 1000m, $"RiskAmount must be 250*4=1000. Actual={assessment.RiskAmount}.");
        Assert(assessment.RiskRewardRatio is double rr && Math.Abs(rr - 3.0) < 1e-9, $"RR must be 3.0. Actual={assessment.RiskRewardRatio}.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
