using System.Drawing;
using System.Linq;
using IQIAIndicator.Core.Observability;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Visualization.Rendering;
using OFT.Rendering.Context;

namespace IQIAIndicator.Visualization.Widgets;

/// <summary>
/// Carte indépendante pour un modèle scientifique (Kalman, OU, DynamicZScore, Volatility, SPRT, ...).
/// N'affiche que ce qui existe déjà dans ScientificModelResult + les événements de trace du pipeline
/// (pour la latence). L'état expanded/collapsed est un état de présentation par carte, piloté par
/// ScientificDashboard (clic sur le header, Sprint 13.5) — aucune donnée n'est recalculée ni
/// modifiée selon cet état, seul ce qui est dessiné change.
/// </summary>
internal static class ModelCard
{
    public const int Width = 300;

    /// <summary>Hauteur de la seule zone d'en-tête (nom + caret + score), cliquable dans les deux
    /// états. C'est aussi la hauteur totale de la carte quand elle est repliée.</summary>
    public const int HeaderHeight = 28;

    public const int ExpandedHeight = 96;
    public const int CollapsedHeight = HeaderHeight;

    public static int HeightFor(bool expanded) => expanded ? ExpandedHeight : CollapsedHeight;

    public static void Draw(
        RenderContext renderContext,
        ScientificModelResult result,
        PipelineTraceRun? trace,
        bool expanded,
        int x,
        int y)
    {
        int height = HeightFor(expanded);
        renderContext.FillRectangle(DashboardTheme.CardBackground, new Rectangle(x, y, Width, height));

        // En-tête : seule zone cliquable (Étape 5). Caret purement visuel (Étape 6) — le vrai
        // hit-test porte sur tout le rectangle d'en-tête, pas sur le glyphe lui-même.
        // ASCII caret - ATAS's text renderer drops many geometric-shape glyphs (see DashboardCanvas note).
        string caret = expanded ? "v" : ">";
        string displayName = DashboardCanvas.SplitPascalCase(result.ModelName.Replace("Model", string.Empty));
        renderContext.DrawString($"{caret} {displayName}", DashboardTheme.HeaderFont, DashboardTheme.TextColor, x + 8, y + 6);
        renderContext.DrawString(DashboardCanvas.FormatDouble(result.Score), DashboardTheme.BodyFont, DashboardTheme.TextColor, x + Width - 60, y + 7);

        if (!expanded)
            return;

        DashboardCanvas.Badge(renderContext, string.Empty, result.Success ? HealthState.Pass : HealthState.Fail, x + 8, y + HeaderHeight);

        int fieldY = y + HeaderHeight + 18;
        DashboardCanvas.Field(renderContext, "Score", DashboardCanvas.FormatDouble(result.Score), x + 8, ref fieldY, valueOffset: 90);
        DashboardCanvas.SmallField(renderContext, "Latency", FormatLatency(result, trace), x + 8, ref fieldY, valueOffset: 90);

        renderContext.DrawString(
            DashboardCanvas.Truncate(result.Explanation, 60),
            DashboardTheme.SmallFont,
            DashboardTheme.SecondaryTextColor,
            x + 8,
            fieldY + 2);
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
