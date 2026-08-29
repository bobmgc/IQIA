using System;
using IQIAIndicator.Core;
using IQIAIndicator.Engine.Risk;
using IQIAIndicator.Visualization.Rendering;

namespace IQIAIndicator.Tests.Visualization;

/// <summary>
/// Sprint 15.25 (Lot 12, Section 19). Unit-level coverage of RiskDashboardPresenter - the pure,
/// RenderContext-free view resolution TradingDashboard's RISK ENGINE panel draws from. TradingDashboard
/// itself cannot be unit-tested (requires OFT.Rendering.Context.RenderContext, only available inside a
/// live ATAS platform - the same distinction ScientificDatasetRealMarketCaptureTests.cs's class doc
/// comment already documents for IQIAIndicator). This file proves the panel's DECISIONS (which text,
/// which state) are correct; TradingDashboard.Draw's job is reduced to drawing already-resolved strings.
/// </summary>
public static class RiskDashboardPresenterTests
{
    public static void RunAll()
    {
        Test01_NoRiskAssessment_ReportsNotAvailable();
        Test02_RejectedAssessment_DisplaysRejectedAndReason();
        Test03_AcceptedAssessment_DisplaysAcceptedAndFields();
        Test04_EsInstrument_DisplaysEsSymbol();
        Test05_MesInstrument_DisplaysMesSymbol();
        Test06_MissingStopLoss_DisplaysNotProducedNeverFabricated();
        Test07_PresentingHasNoSideEffects();
        Test08_Balance_DisplayedSeparatelyFromCapital();
        Test09_Balance_AbsentAccount_ReportsNotAvailable();
        Test10_Balance_AccountPresentButBalanceNull_ReportsNotAvailable();
        Test11_NoRiskAssessment_NoStageError_ReportsPlainNotAvailable();
        Test12_NoRiskAssessment_WithStageError_ReportsAtasErrorAndReason();
        Test13_RiskAssessmentPresent_StageErrorIgnored();
    }

    private static InstrumentRiskSpecification EsSpec() =>
        InstrumentRiskSpecification.FromInstrumentInfo(
            new InstrumentInfo("ES", TickSize: 0.25m, TickValue: 12.5m, PointValue: 50m, Decimals: 2),
            minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static InstrumentRiskSpecification MesSpec() =>
        InstrumentRiskSpecification.FromInstrumentInfo(
            new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
            minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static AccountState Account(decimal initialCapital = 0m, decimal currentEquity = 0m, decimal? currentBalance = null) =>
        new(initialCapital, currentEquity, currentBalance, 0m, 0m, 0m, 0m, 0m);

    private static RiskAssessment Accepted() =>
        new(
            Status: RiskDecisionStatus.ACCEPTED,
            RejectionReasons: Array.Empty<RiskRejectionReason>(),
            Direction: TradeDirection.Buy,
            EntryPrice: 100m,
            StopLoss: 95m,
            TakeProfit: 110m,
            RiskDistance: 5m,
            RewardDistance: 10m,
            RiskPerUnit: 250m,
            RiskBudget: 1000m,
            PositionSize: 4,
            RiskAmount: 1000m,
            RewardAmount: 2000m,
            RiskRewardRatio: 2.0,
            Diagnostics: Array.Empty<string>());

    private static RiskAssessment RejectedInvalidCapital() =>
        new(
            Status: RiskDecisionStatus.REJECTED,
            RejectionReasons: new[] { RiskRejectionReason.INVALID_CAPITAL, RiskRejectionReason.INVALID_EQUITY },
            Direction: TradeDirection.Buy,
            EntryPrice: 100m,
            StopLoss: null,
            TakeProfit: null,
            RiskDistance: null,
            RewardDistance: null,
            RiskPerUnit: null,
            RiskBudget: null,
            PositionSize: null,
            RiskAmount: null,
            RewardAmount: null,
            RiskRewardRatio: null,
            Diagnostics: Array.Empty<string>());

    // TEST 1: Aucun RiskAssessment
    private static void Test01_NoRiskAssessment_ReportsNotAvailable()
    {
        RiskDashboardView view = RiskDashboardPresenter.Present(null, null, null, DateTime.UtcNow);

        Assert(view.StatusKind == RiskDashboardStatusKind.NotAvailable, $"Absent RiskAssessment must be NotAvailable, never REJECTED. Actual={view.StatusKind}.");
        Assert(view.StatusText == "NOT AVAILABLE", $"Actual={view.StatusText}.");
        Assert(view.Instrument == "NOT AVAILABLE", "Absent instrument must report NOT AVAILABLE.");
        Assert(view.Capital == "NOT AVAILABLE", "Absent AccountState must report NOT AVAILABLE for Capital.");
        Assert(view.Entry == "NOT AVAILABLE", "Absent RiskAssessment must report NOT AVAILABLE for Entry too.");
        Assert(view.LastUpdate == "NOT AVAILABLE", "No result means no meaningful 'last update' either.");
    }

    // TEST 2: RiskAssessment REJECTED
    private static void Test02_RejectedAssessment_DisplaysRejectedAndReason()
    {
        RiskDashboardView view = RiskDashboardPresenter.Present(RejectedInvalidCapital(), Account(), EsSpec(), DateTime.UtcNow);

        Assert(view.StatusKind == RiskDashboardStatusKind.Rejected, $"Actual={view.StatusKind}.");
        Assert(view.StatusText == "REJECTED", $"Actual={view.StatusText}.");
        Assert(view.Reason.Contains("INVALID_CAPITAL"), $"Reason must contain the real enum value verbatim. Actual={view.Reason}.");
        Assert(view.Reason.Contains("INVALID_EQUITY"), $"Multiple simultaneous reasons must all be shown. Actual={view.Reason}.");
    }

    // TEST 3: RiskAssessment ACCEPTED
    private static void Test03_AcceptedAssessment_DisplaysAcceptedAndFields()
    {
        RiskDashboardView view = RiskDashboardPresenter.Present(Accepted(), Account(50000m, 50000m), EsSpec(), DateTime.UtcNow);

        Assert(view.StatusKind == RiskDashboardStatusKind.Accepted, $"Actual={view.StatusKind}.");
        Assert(view.StatusText == "ACCEPTED", $"Actual={view.StatusText}.");
        Assert(view.PositionSize == "4 contract(s)", $"Actual={view.PositionSize}.");
        Assert(view.RiskBudget == "$1000.00", $"Actual={view.RiskBudget}.");
        Assert(view.TradeRisk == "$1000.00", $"Actual={view.TradeRisk}.");
        Assert(view.RiskReward == "1 : 2.00", $"Actual={view.RiskReward}.");
        Assert(view.Reason == "NOT AVAILABLE", "An ACCEPTED assessment carries no rejection reason.");
    }

    // TEST 4: ES
    private static void Test04_EsInstrument_DisplaysEsSymbol()
    {
        RiskDashboardView view = RiskDashboardPresenter.Present(Accepted(), Account(), EsSpec(), DateTime.UtcNow);
        Assert(view.Instrument == "ES", $"Must display the real Symbol from InstrumentRiskSpecification, not a deduced/hardcoded value. Actual={view.Instrument}.");
    }

    // TEST 5: MES
    private static void Test05_MesInstrument_DisplaysMesSymbol()
    {
        RiskDashboardView view = RiskDashboardPresenter.Present(Accepted(), Account(), MesSpec(), DateTime.UtcNow);
        Assert(view.Instrument == "MES", $"Actual={view.Instrument}.");
    }

    // TEST 6: StopLoss absent
    private static void Test06_MissingStopLoss_DisplaysNotProducedNeverFabricated()
    {
        RiskAssessment noStopLoss = Accepted() with { StopLoss = null };
        RiskDashboardView view = RiskDashboardPresenter.Present(noStopLoss, Account(), EsSpec(), DateTime.UtcNow);
        Assert(view.StopLoss == "SL NOT PRODUCED", $"Missing StopLoss must never be displayed as a fabricated price. Actual={view.StopLoss}.");
    }

    // TEST 7: Aucun effet de bord
    private static void Test07_PresentingHasNoSideEffects()
    {
        RiskAssessment risk = Accepted();
        AccountState account = Account(50000m, 50000m);
        InstrumentRiskSpecification instrument = EsSpec();
        RiskAssessment riskSnapshot = risk with { };
        AccountState accountSnapshot = account with { };
        InstrumentRiskSpecification instrumentSnapshot = instrument with { };

        RiskDashboardPresenter.Present(risk, account, instrument, DateTime.UtcNow);

        Assert(riskSnapshot == risk, "RiskAssessment must not be mutated by presenting it.");
        Assert(accountSnapshot == account, "AccountState must not be mutated by presenting it.");
        Assert(instrumentSnapshot == instrument, "InstrumentRiskSpecification must not be mutated by presenting it.");
    }

    // TEST 8-10: Balance (Sprint 15.25, Lot 12.12, Problem E) - shown separately from Capital
    // (=InitialCapital, manual configuration) and Equity, sourced from AccountState.CurrentBalance
    // (portfolio?.Balance, ATASAccountStateAdapter.Build, Lot 12.2, protected - unchanged, already
    // computed every bar but never displayed before this lot).
    private static void Test08_Balance_DisplayedSeparatelyFromCapital()
    {
        RiskDashboardView view = RiskDashboardPresenter.Present(
            Accepted(), Account(50000m, 50000m, currentBalance: 50103.92m), EsSpec(), DateTime.UtcNow);

        Assert(view.Balance == "$50103.92", $"Actual={view.Balance}.");
        Assert(view.Capital == "$50000.00", "Capital (InitialCapital) must remain distinct from Balance - never merged.");
        Assert(view.Balance != view.Capital, "Balance and Capital must never be presented as the same field/value.");
    }

    private static void Test09_Balance_AbsentAccount_ReportsNotAvailable()
    {
        RiskDashboardView view = RiskDashboardPresenter.Present(Accepted(), null, EsSpec(), DateTime.UtcNow);
        Assert(view.Balance == "NOT AVAILABLE", $"Absent AccountState must report NOT AVAILABLE for Balance too. Actual={view.Balance}.");
    }

    private static void Test10_Balance_AccountPresentButBalanceNull_ReportsNotAvailable()
    {
        // Mirrors ATASAccountStateAdapter.Build: CurrentBalance is null when portfolio?.Balance itself
        // was unavailable - never fabricated as $0.00.
        RiskDashboardView view = RiskDashboardPresenter.Present(Accepted(), Account(50000m, 50000m, currentBalance: null), EsSpec(), DateTime.UtcNow);
        Assert(view.Balance == "NOT AVAILABLE", $"A genuinely-unavailable Balance must never be shown as a fabricated $0.00. Actual={view.Balance}.");
    }

    // TEST 11-13: riskStageError (Sprint 15.25, Lot 12.12, Problem B/I) - distinguishes "no assessable
    // TradePlan candidate this bar" from "the Risk stage's ATAS-owned reads threw this bar", both of
    // which otherwise leave RiskAssessment/AccountState/InstrumentRiskSpecification identically null.
    private static void Test11_NoRiskAssessment_NoStageError_ReportsPlainNotAvailable()
    {
        RiskDashboardView view = RiskDashboardPresenter.Present(null, null, null, DateTime.UtcNow, riskStageError: null);

        Assert(view.StatusKind == RiskDashboardStatusKind.NotAvailable, $"Actual={view.StatusKind}.");
        Assert(view.StatusText == "NOT AVAILABLE", $"A bar with simply no candidate must keep the plain wording. Actual={view.StatusText}.");
        Assert(view.Reason == "NOT AVAILABLE", $"Actual={view.Reason}.");
    }

    private static void Test12_NoRiskAssessment_WithStageError_ReportsAtasErrorAndReason()
    {
        const string error = "NullReferenceException: Object reference not set to an instance of an object.";
        RiskDashboardView view = RiskDashboardPresenter.Present(null, null, null, DateTime.UtcNow, riskStageError: error);

        Assert(view.StatusKind == RiskDashboardStatusKind.NotAvailable, "An ATAS binding exception must never be presented as REJECTED/ACCEPTED - it means the Risk stage never ran.");
        Assert(view.StatusText == "NOT AVAILABLE (ATAS ERROR)", $"Must be visibly distinct from the plain 'no candidate' wording. Actual={view.StatusText}.");
        Assert(view.Reason == error, $"The exact captured exception must reach the Reason field verbatim - never a generic placeholder. Actual={view.Reason}.");
    }

    private static void Test13_RiskAssessmentPresent_StageErrorIgnored()
    {
        // Sanity check: a stale riskStageError from a PRIOR bar must never leak into a bar that DID
        // produce a real RiskAssessment (IQIAIndicator.cs's Risk stage always clears
        // _latestRiskStageError to null on a successful bar - this only proves the presenter itself
        // never lets a non-null riskStageError override a real, present RiskAssessment).
        RiskDashboardView view = RiskDashboardPresenter.Present(Accepted(), Account(50000m, 50000m), EsSpec(), DateTime.UtcNow, riskStageError: "stale error, must be ignored");

        Assert(view.StatusKind == RiskDashboardStatusKind.Accepted, $"Actual={view.StatusKind}.");
        Assert(view.StatusText == "ACCEPTED", $"Actual={view.StatusText}.");
        Assert(view.Reason == "NOT AVAILABLE", "A real ACCEPTED assessment must never surface a stale riskStageError as its Reason.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
