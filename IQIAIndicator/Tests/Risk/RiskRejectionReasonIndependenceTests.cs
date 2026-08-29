using System;
using IQIAIndicator.Engine.Risk;

namespace IQIAIndicator.Tests.RiskTests;

/// <summary>
/// Sprint 15.25 (Lot 12.12, Problem H). Exercises RiskEngine.Evaluate (Lot 10, protected, unmodified)
/// directly to prove, exactly as the Lot 12.12 brief's Section 8/12 four-case matrix requires, that
/// INVALID_EQUITY/INSTRUMENT_SPEC_INVALID/INVALID_STOP_LOSS are three genuinely INDEPENDENT checks - none
/// is ever produced as a side effect of another, and each fires (or does not fire) purely based on its
/// own inputs. RiskEngineTests.cs (Lot 10) already covers RiskEngine's calculation core broadly; this
/// file targets specifically the brief's own four named scenarios so their exact wording is directly
/// traceable to a test.
/// </summary>
public static class RiskRejectionReasonIndependenceTests
{
    public static void RunAll()
    {
        Case1_OnlyStopLossInvalid_ProducesOnlyInvalidStopLoss();
        Case2_OnlyEquityInvalid_ProducesOnlyInvalidEquity();
        Case3_OnlyInstrumentInvalid_ProducesOnlyInstrumentSpecInvalid();
        Case4_AllThreeInvalid_ProducesAllThreeReasons();
    }

    // ── Fixtures - deliberately "everything else nominally valid" per fixture, so each case isolates
    // exactly one failing input (the brief's own framing: "Vérifier que les trois raisons sont
    // réellement indépendantes"). ──────────────────────────────────────────────────────────────────

    private static AccountState ValidAccount() => new(
        InitialCapital: 50000m, CurrentEquity: 50000m, CurrentBalance: 50000m,
        PeakEquity: 50000m, DailyStartingEquity: 50000m, DailyPnL: 0m, RiskUsedToday: 0m, OpenRisk: 0m);

    private static AccountState InvalidEquityAccount() => ValidAccount() with { CurrentEquity = 0m };

    private static InstrumentRiskSpecification ValidInstrument() => new(
        Symbol: "MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m,
        MinQuantity: 1, MaxQuantity: 50, QuantityStep: 1);

    private static InstrumentRiskSpecification InvalidInstrument() => ValidInstrument() with { QuantityStep = 0 };

    private static RiskPolicy Policy() => new(
        MaxRiskPerTradePercent: 0.02m, MaxRiskPerTradeAmount: null,
        MaxDailyLossPercent: null, MaxDailyLossAmount: null,
        MaxDrawdownPercent: null, MaxDrawdownAmount: null,
        MaxOpenRiskPercent: null, MaxOpenRiskAmount: null,
        MinRiskReward: null, MaxPositionSize: null);

    private static RiskEngineRequest Request(
        AccountState account, InstrumentRiskSpecification instrument, decimal? stopLoss) =>
        new(
            Direction: TradeDirection.Buy,
            EntryPrice: 100m,
            StopLoss: stopLoss,
            TakeProfit: 110m,
            Instrument: instrument,
            Account: account,
            Policy: Policy());

    // ── Case 1: Equity valide, Instrument valide, SL invalide -> uniquement INVALID_STOP_LOSS ────────

    private static void Case1_OnlyStopLossInvalid_ProducesOnlyInvalidStopLoss()
    {
        RiskAssessment assessment = new RiskEngine().Evaluate(
            Request(ValidAccount(), ValidInstrument(), stopLoss: null));

        Assert(assessment.Status == RiskDecisionStatus.REJECTED, $"Actual={assessment.Status}.");
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_STOP_LOSS), "Must contain INVALID_STOP_LOSS.");
        Assert(!assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_EQUITY), $"Must NOT contain INVALID_EQUITY. Actual=[{string.Join(",", assessment.RejectionReasons)}].");
        Assert(!assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_CAPITAL), $"Must NOT contain INVALID_CAPITAL. Actual=[{string.Join(",", assessment.RejectionReasons)}].");
        Assert(!assessment.RejectionReasons.Contains(RiskRejectionReason.INSTRUMENT_SPEC_INVALID), $"Must NOT contain INSTRUMENT_SPEC_INVALID. Actual=[{string.Join(",", assessment.RejectionReasons)}].");
    }

    // ── Case 2: Equity invalide, Instrument valide, SL valide -> uniquement INVALID_EQUITY ───────────

    private static void Case2_OnlyEquityInvalid_ProducesOnlyInvalidEquity()
    {
        RiskAssessment assessment = new RiskEngine().Evaluate(
            Request(InvalidEquityAccount(), ValidInstrument(), stopLoss: 95m));

        Assert(assessment.Status == RiskDecisionStatus.REJECTED, $"Actual={assessment.Status}.");
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_EQUITY), "Must contain INVALID_EQUITY.");
        Assert(!assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_STOP_LOSS), $"Must NOT contain INVALID_STOP_LOSS. Actual=[{string.Join(",", assessment.RejectionReasons)}].");
        Assert(!assessment.RejectionReasons.Contains(RiskRejectionReason.INSTRUMENT_SPEC_INVALID), $"Must NOT contain INSTRUMENT_SPEC_INVALID. Actual=[{string.Join(",", assessment.RejectionReasons)}].");
        // InitialCapital (50000m, valid) is independent from CurrentEquity (0m, invalid here) - Phase 1
        // of RiskEngine.Evaluate checks each separately (Lot 10), so INVALID_CAPITAL must not appear
        // merely because Equity failed.
        Assert(!assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_CAPITAL), $"Must NOT contain INVALID_CAPITAL - InitialCapital itself is valid in this fixture. Actual=[{string.Join(",", assessment.RejectionReasons)}].");
    }

    // ── Case 3: Equity valide, Instrument invalide, SL valide -> uniquement INSTRUMENT_SPEC_INVALID ──

    private static void Case3_OnlyInstrumentInvalid_ProducesOnlyInstrumentSpecInvalid()
    {
        RiskAssessment assessment = new RiskEngine().Evaluate(
            Request(ValidAccount(), InvalidInstrument(), stopLoss: 95m));

        Assert(assessment.Status == RiskDecisionStatus.REJECTED, $"Actual={assessment.Status}.");
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INSTRUMENT_SPEC_INVALID), "Must contain INSTRUMENT_SPEC_INVALID.");
        Assert(!assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_EQUITY), $"Must NOT contain INVALID_EQUITY. Actual=[{string.Join(",", assessment.RejectionReasons)}].");
        Assert(!assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_CAPITAL), $"Must NOT contain INVALID_CAPITAL. Actual=[{string.Join(",", assessment.RejectionReasons)}].");
        Assert(!assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_STOP_LOSS), $"Must NOT contain INVALID_STOP_LOSS - StopLoss (95, correct side of a 100 Buy entry) is valid on its own in this fixture. Actual=[{string.Join(",", assessment.RejectionReasons)}].");
    }

    // ── Case 4: Equity invalide, Instrument invalide, SL invalide -> les trois raisons ───────────────

    private static void Case4_AllThreeInvalid_ProducesAllThreeReasons()
    {
        RiskAssessment assessment = new RiskEngine().Evaluate(
            Request(InvalidEquityAccount(), InvalidInstrument(), stopLoss: null));

        Assert(assessment.Status == RiskDecisionStatus.REJECTED, $"Actual={assessment.Status}.");
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_EQUITY), "Must contain INVALID_EQUITY.");
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INSTRUMENT_SPEC_INVALID), "Must contain INSTRUMENT_SPEC_INVALID.");
        Assert(assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_STOP_LOSS), "Must contain INVALID_STOP_LOSS.");
        Assert(!assessment.RejectionReasons.Contains(RiskRejectionReason.INVALID_CAPITAL), $"InitialCapital itself remains valid in this fixture - must NOT contain INVALID_CAPITAL. Actual=[{string.Join(",", assessment.RejectionReasons)}].");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
