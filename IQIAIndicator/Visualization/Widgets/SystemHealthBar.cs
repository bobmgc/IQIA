using System.Drawing;
using IQIAIndicator.Visualization.Rendering;
using IQIAIndicator.Visualization.State;
using OFT.Rendering.Context;

namespace IQIAIndicator.Visualization.Widgets;

/// <summary>Barre toujours visible résumant la santé de chaque étage du pipeline.</summary>
internal static class SystemHealthBar
{
    public static void Draw(RenderContext renderContext, SystemHealthReport report, int x, int y, int width)
    {
        renderContext.FillRectangle(DashboardTheme.CardBackground, new Rectangle(x, y, width, DashboardLayout.BarHeight));
        renderContext.DrawString("SYSTEM HEALTH", DashboardTheme.SmallFont, DashboardTheme.TextColor, x + 8, y + 4);

        int labelX = x + 130;
        const int slot = 140;

        DashboardCanvas.Badge(renderContext, "Scientific", report.Scientific, labelX, y + 4); labelX += slot;
        DashboardCanvas.Badge(renderContext, "Fusion", report.Fusion, labelX, y + 4); labelX += slot;
        DashboardCanvas.Badge(renderContext, "Decision", report.Decision, labelX, y + 4); labelX += slot;
        DashboardCanvas.Badge(renderContext, "Dataset", report.Dataset, labelX, y + 4); labelX += slot;
        DashboardCanvas.Badge(renderContext, "Renderer", report.Renderer, labelX, y + 4); labelX += slot;
        DashboardCanvas.Badge(renderContext, "Export", report.Export, labelX, y + 4);
    }
}
