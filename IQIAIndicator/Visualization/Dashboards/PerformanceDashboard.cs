using System;
using System.Drawing;
using System.Linq;
using IQIAIndicator.Core.Observability;
using IQIAIndicator.Visualization.Rendering;
using IQIAIndicator.Visualization.State;
using OFT.Rendering.Context;

namespace IQIAIndicator.Visualization.Dashboards;

/// <summary>
/// Temps du pipeline par étage (dernier bar) + agrégats glissants (Average/Max/Min/Rolling/P99)
/// calculés depuis PerformanceHistory. Aucune mesure n'est recalculée : uniquement agrégée.
/// </summary>
internal sealed class PerformanceDashboard
{
    public const int Width = 480;
    public const int Height = 480;
    private const int RollingWindow = 20;

    private static readonly PipelineTraceStage[] StageOrder =
    [
        PipelineTraceStage.MarketContext,
        PipelineTraceStage.ScientificModels,
        PipelineTraceStage.ScientificFusion,
        PipelineTraceStage.Decision,
        PipelineTraceStage.Entry,
        PipelineTraceStage.EntryTrigger,
        PipelineTraceStage.Visualization,
        PipelineTraceStage.ChartAnnotation,
        PipelineTraceStage.Presentation,
        PipelineTraceStage.Renderer
    ];

    private readonly PerformanceHistory _history = new();

    public void Draw(RenderContext renderContext, DashboardContext context, int x, int y)
    {
        renderContext.FillRectangle(DashboardTheme.PanelBackground, new Rectangle(x, y, Width, Height));
        DashboardCanvas.Title(renderContext, "IQIA — PERFORMANCE", x + 10, y + 8);

        if (!context.EnablePipelineTracing)
        {
            renderContext.DrawString("Tracing désactivé (EnablePipelineTracing = false).", DashboardTheme.BodyFont, DashboardTheme.SecondaryTextColor, x + 10, y + 44);
            return;
        }

        _history.Record(context.PipelineTrace);

        int fieldY = y + 42;
        DashboardCanvas.SectionHeader(renderContext, "ÉTAGES (dernier bar)", x + 10, ref fieldY);

        foreach (PipelineTraceStage stage in StageOrder)
        {
            double? elapsed = StageElapsedMs(context.PipelineTrace, stage);
            DashboardCanvas.Field(renderContext, stage.ToString(), DashboardCanvas.FormatMs(elapsed), x + 10, ref fieldY);
        }

        fieldY += 6;
        DashboardCanvas.SectionHeader(renderContext, "PIPELINE TOTAL", x + 10, ref fieldY);

        double[] totals = _history.Runs.Select(run => run.TotalElapsed.TotalMilliseconds).ToArray();
        DashboardCanvas.Field(renderContext, "Dernier", DashboardCanvas.FormatMs(context.PipelineTrace?.TotalElapsed.TotalMilliseconds), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Average", DashboardCanvas.FormatMs(totals.Length == 0 ? null : totals.Average()), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Maximum", DashboardCanvas.FormatMs(totals.Length == 0 ? null : totals.Max()), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Minimum", DashboardCanvas.FormatMs(totals.Length == 0 ? null : totals.Min()), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, $"Rolling Average ({RollingWindow})", DashboardCanvas.FormatMs(RollingAverage(totals)), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "99th percentile", DashboardCanvas.FormatMs(Percentile(totals, 0.99)), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Runs observés", DashboardCanvas.FormatInt(totals.Length), x + 10, ref fieldY);
    }

    private static double? StageElapsedMs(PipelineTraceRun? trace, PipelineTraceStage stage)
    {
        if (trace is null)
            return null;

        var matches = trace.Events.Where(evt => evt.Stage == stage).ToArray();
        return matches.Length == 0 ? null : matches.Sum(evt => evt.Elapsed.TotalMilliseconds);
    }

    private static double? RollingAverage(double[] totals)
    {
        if (totals.Length == 0)
            return null;

        return totals.Skip(Math.Max(0, totals.Length - RollingWindow)).Average();
    }

    private static double? Percentile(double[] values, double probability)
    {
        if (values.Length == 0)
            return null;

        double[] sorted = values.OrderBy(value => value).ToArray();
        int index = (int)Math.Ceiling(probability * (sorted.Length - 1));
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}
