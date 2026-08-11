using System;
using System.Drawing;
using IQIAIndicator.Core;
using IQIAIndicator.Visualization.Rendering;
using OFT.Rendering.Context;

namespace IQIAIndicator.Visualization.Widgets;

/// <summary>
/// Visible uniquement quand ExecutionContext.IsReplay est vrai.
/// L'API ATAS publique n'expose pas de bar cible de replay : "Target" reprend donc le plus
/// grand index de bar actuellement connu (CurrentBar), seule information disponible côté
/// indicateur — pas une vraie cible de fin de relecture fournie par la plateforme.
/// </summary>
internal static class ReplayMonitorWidget
{
    public static void Draw(RenderContext renderContext, ExecutionContext execution, int x, int y, int width)
    {
        renderContext.FillRectangle(DashboardTheme.CardBackground, new Rectangle(x, y, width, DashboardLayout.BarHeight));

        int target = Math.Max(execution.CurrentBar, 1);
        double progress = Math.Clamp((double)execution.LastCalculatedBar / target, 0.0, 1.0);

        renderContext.DrawString("REPLAY", DashboardTheme.SmallFont, DashboardTheme.Accent, x + 8, y + 4);
        renderContext.DrawString("Running", DashboardTheme.SmallFont, DashboardTheme.TextColor, x + 70, y + 4);
        renderContext.DrawString($"Current Bar {execution.LastCalculatedBar}", DashboardTheme.SmallFont, DashboardTheme.SecondaryTextColor, x + 150, y + 4);
        renderContext.DrawString($"Target {target}", DashboardTheme.SmallFont, DashboardTheme.SecondaryTextColor, x + 330, y + 4);
        renderContext.DrawString($"Progress {DashboardCanvas.FormatPercent(progress)}", DashboardTheme.SmallFont, DashboardTheme.StateColor(progress), x + 450, y + 4);
    }
}
