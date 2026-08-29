using System;
using System.Collections.Generic;
using System.Globalization;
using IQIAIndicator.Engine.Risk;

namespace IQIAIndicator.Visualization.Rendering;

/// <summary>Sprint 15.25 (Lot 12). Semantic status of the RISK ENGINE panel - distinct from
/// RiskDecisionStatus (ACCEPTED/REJECTED, Lot 10): NotAvailable is a third state RiskDecisionStatus
/// cannot represent on its own (Lot 12, Section 8) - the Risk Engine has not produced a result at all
/// this bar (no assessable TradePlan candidate), as opposed to having run and rejected one.</summary>
internal enum RiskDashboardStatusKind
{
    NotAvailable,
    Rejected,
    Accepted
}

/// <summary>
/// Sprint 15.25 (Lot 12). Pure text/state view of the Risk Engine's latest output - no RenderContext,
/// no ATAS dependency, so it is directly unit-testable (mirrors TradePlanAnnotationCandidate's role:
/// TradingDashboard only draws these already-resolved strings, never resolves them itself).
/// </summary>
internal sealed record RiskDashboardView(
    RiskDashboardStatusKind StatusKind,
    string StatusText,
    string Instrument,
    string Capital,
    string Balance,
    string Equity,
    string RiskBudget,
    string TradeRisk,
    string PositionSize,
    string Entry,
    string StopLoss,
    string TakeProfit,
    string RiskReward,
    string Reason,
    string LastUpdate);

/// <summary>
/// Sprint 15.25 (Lot 12). Resolves a RISK ENGINE dashboard view from the same objects the pipeline
/// already produced this bar (RiskAssessment/AccountState/InstrumentRiskSpecification) - never
/// recomputes risk, sizing, or R:R (Lot 12, Section 5): every numeric value here is a direct read from
/// an existing RiskAssessment/AccountState/InstrumentRiskSpecification field, formatted only. A field
/// genuinely unavailable is reported as the literal "NOT AVAILABLE" (mirrors ScientificDatasetRecord's
/// Lot 9/11 convention) - never a fabricated 0/N/A that could be mistaken for a real value.
/// </summary>
internal static class RiskDashboardPresenter
{
    private const string NotAvailable = "NOT AVAILABLE";
    private const string StopLossNotProduced = "SL NOT PRODUCED";
    // Sprint 15.25 (Lot 12.12, Problem I): a THIRD distinct status text, never confused with the plain
    // "NOT AVAILABLE" a bar with no assessable TradePlan candidate already reports - both leave every
    // numeric field NOT AVAILABLE, but only this one means the Risk stage itself never even ran this
    // bar (an ATAS binding exception, not "nothing to evaluate" - see IQIAIndicator.cs's Risk stage
    // catch clause).
    private const string NotAvailableAtasError = "NOT AVAILABLE (ATAS ERROR)";

    /// <summary>Sprint 15.25 (Lot 12.12): <paramref name="riskStageError"/> is optional/trailing (default
    /// null) - every pre-existing call site keeps compiling unchanged. Non-null only distinguishes WHY
    /// risk/account/instrument are all null this bar (Problem I: "le dashboard ne doit jamais afficher
    /// NOT AVAILABLE simplement parce qu'un objet n'a pas été correctement propagé" - this makes the
    /// real cause visible instead of collapsing every null-input bar into the same generic text).</summary>
    public static RiskDashboardView Present(
        RiskAssessment? risk,
        AccountState? account,
        InstrumentRiskSpecification? instrument,
        DateTime timestamp,
        string? riskStageError = null)
    {
        (RiskDashboardStatusKind statusKind, string statusText) = (risk, riskStageError) switch
        {
            (null, not null) => (RiskDashboardStatusKind.NotAvailable, NotAvailableAtasError),
            (null, null) => (RiskDashboardStatusKind.NotAvailable, NotAvailable),
            ({ Status: RiskDecisionStatus.ACCEPTED }, _) => (RiskDashboardStatusKind.Accepted, "ACCEPTED"),
            _ => (RiskDashboardStatusKind.Rejected, "REJECTED")
        };

        string reason = risk is null
            ? (riskStageError ?? NotAvailable)
            : FormatReasons(risk.RejectionReasons);

        return new RiskDashboardView(
            StatusKind: statusKind,
            StatusText: statusText,
            Instrument: instrument?.Symbol is string symbol && !string.IsNullOrWhiteSpace(symbol) ? symbol : NotAvailable,
            Capital: account is null ? NotAvailable : FormatMoney(account.InitialCapital),
            // Sprint 15.25 (Lot 12.12, Problem E): AccountState.CurrentBalance (portfolio?.Balance,
            // ATASAccountStateAdapter.Build, Lot 12.2, protected - unchanged) was already computed every
            // bar but never displayed, leaving "Capital" (=InitialCapital, manual configuration) as the
            // only account-money field shown - risking exactly the confusion Problem E forbids ("ne pas
            // appeler Capital une donnée qui est en réalité Balance"). Shown separately, never merged.
            Balance: account?.CurrentBalance is decimal balance ? FormatMoney(balance) : NotAvailable,
            Equity: account is null ? NotAvailable : FormatMoney(account.CurrentEquity),
            RiskBudget: risk?.RiskBudget is decimal riskBudget ? FormatMoney(riskBudget) : NotAvailable,
            TradeRisk: risk?.RiskAmount is decimal riskAmount ? FormatMoney(riskAmount) : NotAvailable,
            PositionSize: risk?.PositionSize is int positionSize ? $"{positionSize} contract(s)" : NotAvailable,
            Entry: risk is null ? NotAvailable : FormatPrice(risk.EntryPrice),
            StopLoss: risk?.StopLoss is decimal stopLoss ? FormatPrice(stopLoss) : StopLossNotProduced,
            TakeProfit: risk?.TakeProfit is decimal takeProfit ? FormatPrice(takeProfit) : NotAvailable,
            RiskReward: risk?.RiskRewardRatio is double rr && double.IsFinite(rr) ? $"1 : {rr.ToString("0.00", CultureInfo.InvariantCulture)}" : NotAvailable,
            Reason: reason,
            LastUpdate: risk is null ? NotAvailable : timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
    }

    private static string FormatMoney(decimal value) => $"${value.ToString("0.00", CultureInfo.InvariantCulture)}";

    private static string FormatPrice(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static string FormatReasons(IReadOnlyList<RiskRejectionReason>? reasons) =>
        reasons is null || reasons.Count == 0 ? NotAvailable : string.Join(", ", reasons);
}
