using System.Linq;
using IQIAIndicator.Core.Observability;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Visualization.Rendering;
using OFT.Rendering.Context;

namespace IQIAIndicator.Visualization.Widgets;

/// <summary>
/// Carte indépendante pour un modèle scientifique (Kalman, OU, DynamicZScore, Volatility, SPRT, ...).
/// N'affiche que ce qui existe déjà dans ScientificModelResult + les événements de trace du pipeline
/// (pour la latence). Les diagnostics/metrics détaillés sont repliables.
/// </summary>
internal static class ModelCard
{
    public const int Width = 300;
    public const int Height = 96;

    public static void Draw(
        RenderContext renderContext,
        ScientificModelResult result,
        PipelineTraceRun? trace,
        bool expanded,
        int x,
        int y)
    {
        renderContext.FillRectangle(DashboardTheme.CardBackground, new System.Drawing.Rectangle(x, y, Width, Height));

        string displayName = DashboardCanvas.SplitPascalCase(result.ModelName.Replace("Model", string.Empty));
        renderContext.DrawString(displayName, DashboardTheme.HeaderFont, DashboardTheme.TextColor, x + 8, y + 6);

        DashboardCanvas.Badge(renderContext, string.Empty, result.Success ? HealthState.Pass : HealthState.Fail, x + 8, y + 24);

        int fieldY = y + 42;
        DashboardCanvas.SmallField(renderContext, "Score", DashboardCanvas.FormatDouble(result.Score), x + 8, ref fieldY, valueOffset: 90);
        DashboardCanvas.SmallField(renderContext, "Confidence", FormatConfidence(result), x + 8, ref fieldY, valueOffset: 90);
        DashboardCanvas.SmallField(renderContext, "Latency", FormatLatency(result, trace), x + 8, ref fieldY, valueOffset: 90);

        int detailY = fieldY;
        CollapsiblePanel.Header(renderContext, "Diagnostics", expanded, x + 8, ref detailY);
        if (expanded)
        {
            renderContext.DrawString(
                DashboardCanvas.Truncate(result.Explanation, 60),
                DashboardTheme.SmallFont,
                DashboardTheme.SecondaryTextColor,
                x + 8,
                detailY);
        }
    }

    /// <summary>ScientificModelResult n'expose pas de Confidence dédiée : on reprend la première
    /// métrique de Metrics dont le nom se termine par "Confidence" (ex. SPRTConfidence, DynamicConfidence).</summary>
    private static string FormatConfidence(ScientificModelResult result)
    {
        if (result.Metrics is null)
            return "N/A";

        var match = result.Metrics
            .FirstOrDefault(entry => entry.Key.EndsWith("Confidence", System.StringComparison.OrdinalIgnoreCase));

        return match.Value is double value ? DashboardCanvas.FormatDouble(value) : "N/A";
    }

    private static string FormatLatency(ScientificModelResult result, PipelineTraceRun? trace)
    {
        if (trace is null)
            return "N/A";

        var matchingEvent = trace.Events
            .Where(evt => evt.Stage == PipelineTraceStage.ScientificModels)
            .FirstOrDefault(evt => evt.Details.TryGetValue("Model", out string? name) && name == result.ModelName);

        return matchingEvent is null ? "N/A" : $"{matchingEvent.Elapsed.TotalMilliseconds:0.000} ms";
    }
}
