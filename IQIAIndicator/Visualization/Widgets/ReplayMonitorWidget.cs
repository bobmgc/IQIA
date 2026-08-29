using System;
using System.Drawing;
using IQIAIndicator.Core;
using IQIAIndicator.Visualization.Rendering;
using OFT.Rendering.Context;

namespace IQIAIndicator.Visualization.Widgets;

/// <summary>
/// Visible uniquement quand ExecutionContext.IsReplay est vrai.
/// ExecutionContext n'expose ni état Running/Paused/Stopped/Error, ni bar cible de fin de
/// replay : le statut est donc affiché comme UNKNOWN plutôt que comme un fait, et "Bars Known"
/// reprend le plus grand index de bar actuellement connu (CurrentBar) sans le présenter comme
/// une vraie cible de fin de relecture fournie par la plateforme.
/// </summary>
internal static class ReplayMonitorWidget
{
    public static void Draw(RenderContext renderContext, ExecutionContext execution, int x, int y, int width)
    {
        renderContext.FillRectangle(DashboardTheme.CardBackground, new Rectangle(x, y, width, DashboardLayout.BarHeight));

        int barsKnown = Math.Max(execution.CurrentBar, 1);
        double progress = Math.Clamp((double)execution.LastCalculatedBar / barsKnown, 0.0, 1.0);

        renderContext.DrawString("REPLAY", DashboardTheme.SmallFont, DashboardTheme.Accent, x + 8, y + 4);
        renderContext.DrawString("UNKNOWN", DashboardTheme.SmallFont, DashboardTheme.Gray, x + 70, y + 4);
        renderContext.DrawString($"Current Bar {execution.LastCalculatedBar}", DashboardTheme.SmallFont, DashboardTheme.SecondaryTextColor, x + 150, y + 4);
        renderContext.DrawString($"Bars Known {barsKnown}", DashboardTheme.SmallFont, DashboardTheme.SecondaryTextColor, x + 330, y + 4);
        renderContext.DrawString($"Progress {DashboardCanvas.FormatPercent(progress)}", DashboardTheme.SmallFont, DashboardTheme.StateColor(progress), x + 450, y + 4);
    }
}
