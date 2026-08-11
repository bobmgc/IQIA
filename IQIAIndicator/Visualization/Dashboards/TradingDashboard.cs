using System.Drawing;
using IQIAIndicator.Visualization.Rendering;
using IQIAIndicator.Visualization.State;
using OFT.Rendering.Context;

namespace IQIAIndicator.Visualization.Dashboards;

/// <summary>
/// Vue trader : régime, confiance globale, score final, décision, risque, signal,
/// méthodologie, opportunité. Aucune donnée scientifique détaillée.
/// </summary>
internal sealed class TradingDashboard
{
    public const int Width = 340;
    public const int Height = 420;

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
        DashboardCanvas.Field(renderContext, "Score final", DashboardCanvas.FormatDouble(context.DecisionResult?.WinnerScore), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Méthodologie", context.MethodologySelection?.SelectedMethodology.Name ?? "N/A", x + 10, ref fieldY);

        fieldY += 6;
        DashboardCanvas.SectionHeader(renderContext, "DÉCISION", x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Signal", context.OpportunityPresentation?.SignalLabel ?? "Not Available", x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Risque", context.OpportunityPresentation?.RiskLabel ?? "Not Available", x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Statut", context.OpportunityPresentation?.OpportunityStatus ?? "N/A", x + 10, ref fieldY);

        fieldY += 6;
        DashboardCanvas.SectionHeader(renderContext, "OPPORTUNITÉ", x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Readiness", context.EntryCandidate?.Assessment.EntryReadiness.ToString() ?? "N/A", x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Priority", DashboardCanvas.FormatDouble(context.EntryCandidate?.OpportunityPriority), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Trigger", context.EntryTriggerCandidate?.Assessment.TriggerStatus.ToString() ?? "N/A", x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Direction", context.EntryTriggerCandidate?.Assessment.Direction.ToString() ?? "N/A", x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Équilibre estimé", DashboardCanvas.FormatDouble(context.EntryTriggerCandidate?.Assessment.EstimatedEquilibrium), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Distance équilibre", DashboardCanvas.FormatDouble(context.EntryTriggerCandidate?.Assessment.DistanceToEquilibrium), x + 10, ref fieldY);

        fieldY += 6;
        DashboardCanvas.SectionHeader(renderContext, "SUPPORT / BLOCAGE", x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Supporting", DashboardCanvas.FormatList(context.EntryCandidate?.Assessment.SupportingEvidence), x + 10, ref fieldY, valueOffset: 120);
        DashboardCanvas.Field(renderContext, "Blocking", DashboardCanvas.FormatList(context.EntryCandidate?.Assessment.BlockingIssues), x + 10, ref fieldY, valueOffset: 120);
    }
}
