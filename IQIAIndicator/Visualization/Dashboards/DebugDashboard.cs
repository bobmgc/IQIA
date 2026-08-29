using System.Drawing;
using System.Linq;
using IQIAIndicator.Visualization.Rendering;
using IQIAIndicator.Visualization.State;
using OFT.Rendering.Context;

namespace IQIAIndicator.Visualization.Dashboards;

/// <summary>
/// Seul endroit où atterrissent les informations développeur qui n'ont leur place nulle part
/// ailleurs : télémétrie renderer, ExecutionContext brut, rapport de trace texte,
/// inspection d'un enregistrement brut du dataset.
/// </summary>
internal sealed class DebugDashboard
{
    public const int Width = 900;
    // Sprint 15.25 (Lot 12.12, Problem A/B): +56px for the new "ATAS CONTEXT (RÉSOLU)" section (header,
    // 18px + 2 fields, 16px each = 32px + the 6px inter-section gap already used by every section below).
    public const int Height = 676;

    public void Draw(RenderContext renderContext, DashboardContext context, int x, int y)
    {
        renderContext.FillRectangle(DashboardTheme.PanelBackground, new Rectangle(x, y, Width, Height));
        DashboardCanvas.Title(renderContext, "IQIA — DEBUG", x + 10, y + 8);

        int fieldY = y + 42;
        DashboardCanvas.SectionHeader(renderContext, "EXECUTION CONTEXT", x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "CurrentBar", DashboardCanvas.FormatInt(context.Execution?.CurrentBar), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "LastCalculatedBar", DashboardCanvas.FormatInt(context.Execution?.LastCalculatedBar), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "IsRealtime", context.Execution?.IsRealtime.ToString() ?? "N/A", x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "IsHistorical", context.Execution?.IsHistorical.ToString() ?? "N/A", x + 10, ref fieldY);
        // Sprint 15.25 (Lot 12.12, Problem A): relabelled to make explicit this is the raw, in-house
        // bar-index heuristic (Core.MarketContextBuilder) - independently proven unreliable on its own
        // (a real Replay capture showed it False during an actual Replay session, and True during a
        // genuine live session, Lot 12.10/12.12 reports). Never used alone anywhere in the pipeline
        // since Lot 12.11 - see "ATAS Context (résolu)" below for the corrected answer.
        DashboardCanvas.Field(renderContext, "IsReplay (heuristique brute)", context.Execution?.IsReplay.ToString() ?? "N/A", x + 10, ref fieldY);

        fieldY += 6;
        DashboardCanvas.SectionHeader(renderContext, "ATAS CONTEXT (RÉSOLU)", x + 10, ref fieldY);
        // Sprint 15.25 (Lot 12.12, Problem A/C): the corrected Live/Replay resolution (Portfolio
        // .IsReplay() priority, wall-clock guard, heuristic fallback - ATASEquityReplayDetector.cs,
        // Lot 12.6/12.11) propagated to every dashboard consumer, not just Equity source selection -
        // see AtasDataContextResolver.cs and the Lot 12.12 report, Problem A/C.
        DashboardCanvas.Field(renderContext, "Context", context.AtasContext.ToString(), x + 10, ref fieldY);
        // Sprint 15.25 (Lot 12.12, Problem B): non-N/A only when the Risk stage's ATAS-owned reads threw
        // on the most recent bar - see IQIAIndicator.cs's Risk stage catch clause and RiskDashboardPresenter.
        DashboardCanvas.Field(
            renderContext,
            "Risk Stage Error",
            context.RiskStageError ?? "Aucune",
            x + 10,
            ref fieldY,
            context.RiskStageError is null ? DashboardTheme.Green : DashboardTheme.Red,
            valueOffset: 150);

        fieldY += 6;
        DashboardCanvas.SectionHeader(renderContext, "ATAS RENDERER", x + 10, ref fieldY);
        DashboardCanvas.StatusLine(renderContext, "Renderer Called", context.RendererCalled, x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Annotations Rendered", DashboardCanvas.FormatInt(context.AnnotationsRendered), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Last Render Time", DashboardCanvas.FormatDate(context.LastRenderTime), x + 10, ref fieldY);

        fieldY += 6;
        DashboardCanvas.SectionHeader(renderContext, "CHART ANNOTATIONS", x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Annotation Count", DashboardCanvas.FormatInt(context.ChartAnnotationCandidate?.Annotations.Count ?? 0), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Warnings", DashboardCanvas.FormatList(context.ChartAnnotationCandidate?.Warnings), x + 10, ref fieldY, valueOffset: 150);
        DashboardCanvas.Field(renderContext, "Diagnostics", DashboardCanvas.FormatList(context.ChartAnnotationCandidate?.Diagnostics), x + 10, ref fieldY, valueOffset: 150);

        fieldY += 6;
        DashboardCanvas.SectionHeader(renderContext, "DATASET — DERNIER RECORD", x + 10, ref fieldY);
        var lastRecord = context.DatasetCollector?.Records.LastOrDefault();
        if (lastRecord is null)
        {
            renderContext.DrawString("Aucun enregistrement.", DashboardTheme.SmallFont, DashboardTheme.SecondaryTextColor, x + 10, fieldY);
            fieldY += 13;
        }
        else
        {
            DashboardCanvas.SmallField(renderContext, "Bar", DashboardCanvas.FormatInt(lastRecord.CurrentBar), x + 10, ref fieldY, valueOffset: 100);
            DashboardCanvas.SmallField(renderContext, "Timestamp", DashboardCanvas.FormatDate(lastRecord.Timestamp), x + 10, ref fieldY, valueOffset: 100);
            DashboardCanvas.SmallField(renderContext, "Metrics Count", DashboardCanvas.FormatInt(lastRecord.Metrics.Count), x + 10, ref fieldY, valueOffset: 100);
            DashboardCanvas.SmallField(renderContext, "Categories Count", DashboardCanvas.FormatInt(lastRecord.Categories.Count), x + 10, ref fieldY, valueOffset: 100);
        }

        fieldY += 10;
        DashboardCanvas.SectionHeader(renderContext, "TRACE REPORT (texte brut)", x + 10, ref fieldY);
        DrawWrappedText(renderContext, context.PipelineTraceReport, x + 10, ref fieldY, y + Height - 20, 8);
    }

    private static void DrawWrappedText(RenderContext renderContext, string report, int x, ref int y, int maxY, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(report))
        {
            renderContext.DrawString("Tracing désactivé ou aucun rapport disponible.", DashboardTheme.SmallFont, DashboardTheme.SecondaryTextColor, x, y);
            return;
        }

        string[] lines = report.Split('\n');
        int drawn = 0;
        foreach (string line in lines)
        {
            if (drawn >= maxLines || y >= maxY)
            {
                renderContext.DrawString("… (voir LastPipelineTraceReport pour le rapport complet)", DashboardTheme.SmallFont, DashboardTheme.MutedTextColor, x, y);
                return;
            }

            renderContext.DrawString(line.TrimEnd('\r'), DashboardTheme.SmallFont, DashboardTheme.SecondaryTextColor, x, y);
            y += 13;
            drawn++;
        }
    }
}
