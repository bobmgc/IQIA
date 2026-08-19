using System.Drawing;
using IQIAIndicator.Engine.ScientificFusion;
using IQIAIndicator.Engine.TradePlan;
using IQIAIndicator.Visualization.Rendering;
using IQIAIndicator.Visualization.State;
using OFT.Rendering.Context;

namespace IQIAIndicator.Visualization.Dashboards;

/// <summary>
/// Vue trader : régime, confiance globale, score final, décision, risque, signal,
/// méthodologie, opportunité, plan de trade, Risk Engine. Aucune donnée scientifique détaillée.
/// </summary>
internal sealed class TradingDashboard
{
    public const int Width = 740;

    // Sprint 15.25 (Lot 12): 600 (Lot <15.24 baseline) + ~232 for the new RISK ENGINE section (header +
    // 13 fields) below TRADE PLAN.
    // Sprint 15.25 (Lot 12.12, Problem E): +16px for the new "Balance" field (14 fields total).
    public const int Height = 836;

    public void Draw(RenderContext renderContext, DashboardContext context, int x, int y)
    {
        renderContext.FillRectangle(DashboardTheme.PanelBackground, new Rectangle(x, y, Width, Height));
        DashboardCanvas.Title(renderContext, "IQIA — TRADING", x + 10, y + 8);

        int fieldY = y + 40;
        DashboardCanvas.SectionHeader(renderContext, "MARCHÉ", x + 10, ref fieldY);

        string regime = context.DecisionResult is null
            ? "N/A"
            : DashboardCanvas.SplitPascalCase(context.DecisionResult.Winner.ToString());
        DashboardCanvas.Field(renderContext, "Régime", regime, x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Confiance globale", DashboardCanvas.FormatPercent(context.ScientificAssessment?.OverallConfidence ?? 0.0), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Score composite", DashboardCanvas.FormatDouble(context.DecisionResult?.WinnerScore), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Méthodologie", context.MethodologySelection?.SelectedMethodology.Name ?? "N/A", x + 10, ref fieldY);

        (string coverageText, Color? coverageColor) = ScientificEvidenceVisual(context.ScientificAssessment);
        DashboardCanvas.Field(renderContext, "Scientific Evidence", coverageText, x + 10, ref fieldY, coverageColor);

        fieldY += 6;
        DashboardCanvas.SectionHeader(renderContext, "DÉCISION", x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Signal", context.OpportunityPresentation?.SignalLabel ?? "Not Available", x + 10, ref fieldY);

        string riskLabel = context.OpportunityPresentation?.RiskLabel ?? "Not Available";
        (Color riskColor, string riskMarker) = RiskVisual(context.OpportunityPresentation?.RiskLabel);
        DashboardCanvas.Field(renderContext, "Risque", $"{riskMarker} {riskLabel}", x + 10, ref fieldY, riskColor);

        DashboardCanvas.Field(renderContext, "Statut", context.OpportunityPresentation?.OpportunityStatus ?? "N/A", x + 10, ref fieldY);

        fieldY += 6;
        DashboardCanvas.SectionHeader(renderContext, "OPPORTUNITÉ", x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Readiness", DashboardCanvas.HumanizeEnumName(context.EntryCandidate?.Assessment.EntryReadiness.ToString()), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Priority", DashboardCanvas.FormatDouble(context.EntryCandidate?.OpportunityPriority), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Trigger", DashboardCanvas.HumanizeEnumName(context.EntryTriggerCandidate?.Assessment.TriggerStatus.ToString()), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Direction", DashboardCanvas.HumanizeEnumName(context.EntryTriggerCandidate?.Assessment.Direction.ToString()), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Reason", DashboardCanvas.HumanizeEnumName(context.EntryTriggerCandidate?.Assessment.Reason.ToString()), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Équilibre estimé", DashboardCanvas.FormatDouble(context.EntryTriggerCandidate?.Assessment.EstimatedEquilibrium), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Distance équilibre", DashboardCanvas.FormatDouble(context.EntryTriggerCandidate?.Assessment.DistanceToEquilibrium), x + 10, ref fieldY);

        fieldY += 6;
        DashboardCanvas.SectionHeader(renderContext, "SUPPORT / BLOCAGE", x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Supporting", DashboardCanvas.FormatList(context.EntryCandidate?.Assessment.SupportingEvidence), x + 10, ref fieldY, valueOffset: 120);
        DashboardCanvas.Field(renderContext, "Blocking", DashboardCanvas.FormatList(context.EntryCandidate?.Assessment.BlockingIssues), x + 10, ref fieldY, valueOffset: 120);

        fieldY += 6;
        DashboardCanvas.SectionHeader(renderContext, "TRADE PLAN", x + 10, ref fieldY);
        TradePlan? plan = context.TradePlan;
        DashboardCanvas.Field(renderContext, "Direction", DashboardCanvas.HumanizeEnumName(plan?.Direction.ToString()), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Entry", DashboardCanvas.FormatDecimal(plan?.EntryPrice), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Stop Loss", DashboardCanvas.FormatDecimal(plan?.StopLoss), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Take Profit", DashboardCanvas.FormatDecimal(plan?.TakeProfit), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Risk / unit", FormatMoney(plan?.RiskPerUnit), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Risk / trade", FormatMoney(plan?.RiskAmount), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Position", plan?.PositionSize is int size ? $"{size} contract(s)" : "N/A", x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "R:R", FormatRiskReward(plan?.RiskRewardRatio), x + 10, ref fieldY);

        (Color statusColor, string statusMarker) = TradePlanStatusVisual(plan?.Status);
        DashboardCanvas.Field(renderContext, "Status", $"{statusMarker} {DashboardCanvas.HumanizeEnumName(plan?.Status.ToString())}", x + 10, ref fieldY, statusColor);

        // Sprint 15.25 (Lot 12): RISK ENGINE panel. Pure display - every value below is a direct read
        // from RiskDashboardPresenter.Present's output (itself a direct, unrecomputed read of
        // RiskAssessment/AccountState/InstrumentRiskSpecification - see that file's doc comment). No
        // risk/sizing/R:R formula lives here or is duplicated from Engine/Risk.
        fieldY += 6;
        DashboardCanvas.SectionHeader(renderContext, "RISK ENGINE", x + 10, ref fieldY);
        RiskDashboardView riskView = RiskDashboardPresenter.Present(
            context.RiskAssessment, context.RiskAccount, context.RiskInstrument, context.Timestamp, context.RiskStageError);

        Color riskStatusColor = riskView.StatusKind switch
        {
            RiskDashboardStatusKind.Accepted => DashboardTheme.Green,
            RiskDashboardStatusKind.Rejected => DashboardTheme.Red,
            _ => DashboardTheme.Gray
        };
        DashboardCanvas.Field(renderContext, "Status", riskView.StatusText, x + 10, ref fieldY, riskStatusColor);
        DashboardCanvas.Field(renderContext, "Instrument", riskView.Instrument, x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Capital", riskView.Capital, x + 10, ref fieldY);
        // Sprint 15.25 (Lot 12.12, Problem E): shown separately from Capital (=InitialCapital, manual
        // configuration) and Equity - see RiskDashboardPresenter.Present's doc comment.
        DashboardCanvas.Field(renderContext, "Balance", riskView.Balance, x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Equity", riskView.Equity, x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Risk Budget", riskView.RiskBudget, x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Trade Risk", riskView.TradeRisk, x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Position Size", riskView.PositionSize, x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Entry", riskView.Entry, x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Stop Loss", riskView.StopLoss, x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Take Profit", riskView.TakeProfit, x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "R:R", riskView.RiskReward, x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Reason", riskView.Reason, x + 10, ref fieldY, riskView.StatusKind == RiskDashboardStatusKind.Rejected ? DashboardTheme.Red : null, valueOffset: 120);
        DashboardCanvas.Field(renderContext, "Last Update", riskView.LastUpdate, x + 10, ref fieldY);
    }

    /// <summary>Sprint 15.8: money fields (RiskPerUnit/RiskAmount) are account-currency amounts, distinct
    /// from FormatDecimal's raw price formatting - kept separate so a future currency symbol change
    /// only touches this one place.</summary>
    private static string FormatMoney(decimal? value) => value is null ? "N/A" : $"${value.Value:0.00}";

    /// <summary>Sprint 15.8: "1 : X.XX" only when the ratio was actually computed (see
    /// TradePlanBuilder Phase 8 - never divides by a non-positive risk); otherwise honestly N/A rather
    /// than a fabricated ratio.</summary>
    private static string FormatRiskReward(double? riskRewardRatio) =>
        riskRewardRatio is double rr && double.IsFinite(rr) ? $"1 : {rr:0.00}" : "N/A";

    /// <summary>Sprint 15.8: maps TradePlanStatus to a state color, mirroring RiskVisual's pattern
    /// below. PLAN_READY is the only state that represents an honestly complete, tradable plan.</summary>
    private static (Color Color, string Marker) TradePlanStatusVisual(TradePlanStatus? status) => status switch
    {
        TradePlanStatus.PLAN_READY => (DashboardTheme.Green, "●"),
        TradePlanStatus.SIGNAL_ONLY => (DashboardTheme.Orange, "●"),
        TradePlanStatus.PLAN_BLOCKED => (DashboardTheme.Red, "●"),
        TradePlanStatus.NO_TRADE => (DashboardTheme.Gray, "●"),
        _ => (DashboardTheme.Gray, "●")
    };

    /// <summary>Sprint 14 (audit finding ARC-003 / Part D): makes it honestly visible when the
    /// current regime has no scientific model registered at all - as opposed to models having run
    /// and found insufficient data - instead of letting it silently read as an ordinary "N/A".
    /// Only reports the two states ScientificAssessmentBuilder actually distinguishes; invents no
    /// new score or confidence value.</summary>
    private static (string Text, Color? Color) ScientificEvidenceVisual(ScientificAssessment? scientificAssessment) =>
        scientificAssessment switch
        {
            null => ("N/A", null),
            { CoverageStatus: ScientificCoverageStatus.NoModelCoverage } => ("NO MODEL COVERAGE", DashboardTheme.Orange),
            _ => ("Models Executed", null)
        };

    /// <summary>Mappe les valeurs réelles de RiskLabel (OpportunityPresentationBuilder.DetermineRiskLabel)
    /// vers une couleur d'état, pour rendre le risque immédiatement identifiable. Fallback neutre pour
    /// toute valeur non reconnue : aucune nouvelle catégorie de risque n'est introduite.</summary>
    private static (Color Color, string Marker) RiskVisual(string? riskLabel) => riskLabel switch
    {
        "Risque faible" => (DashboardTheme.Green, "●"),
        "Risque moyen" => (DashboardTheme.Orange, "●"),
        "Risque élevé" => (DashboardTheme.Red, "●"),
        "Risque accru" => (DashboardTheme.Red, "●"),
        _ => (DashboardTheme.Gray, "●")
    };
}
