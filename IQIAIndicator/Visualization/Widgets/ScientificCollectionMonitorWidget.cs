using System.Drawing;
using IQIAIndicator.Core.Calibration;
using IQIAIndicator.Visualization.Rendering;
using OFT.Rendering.Context;

namespace IQIAIndicator.Visualization.Widgets;

/// <summary>Visible en permanence quand EnableScientificDataset est activé.</summary>
internal static class ScientificCollectionMonitorWidget
{
    public static void Draw(
        RenderContext renderContext,
        ScientificDatasetCollector? collector,
        ScientificDatasetSession? lastSession,
        int currentBar,
        int x,
        int y,
        int width)
    {
        renderContext.FillRectangle(DashboardTheme.CardBackground, new Rectangle(x, y, width, DashboardLayout.BarHeight));

        renderContext.DrawString("COLLECTION", DashboardTheme.SmallFont, DashboardTheme.Accent, x + 8, y + 4);
        renderContext.DrawString("Recording ON", DashboardTheme.SmallFont, DashboardTheme.Green, x + 95, y + 4);
        renderContext.DrawString($"Current Bar {currentBar}", DashboardTheme.SmallFont, DashboardTheme.SecondaryTextColor, x + 210, y + 4);
        renderContext.DrawString($"Accepted {collector?.RecordsAccepted ?? 0}", DashboardTheme.SmallFont, DashboardTheme.Green, x + 380, y + 4);
        renderContext.DrawString($"Duplicates {collector?.DuplicateRecordsRejected ?? 0}", DashboardTheme.SmallFont, DashboardTheme.Orange, x + 490, y + 4);
        renderContext.DrawString($"CSV {(lastSession?.CSVPath is null ? "Pending" : "Exported")}", DashboardTheme.SmallFont, DashboardTheme.SecondaryTextColor, x + 630, y + 4);
        renderContext.DrawString($"JSON {(lastSession?.JSONPath is null ? "Pending" : "Exported")}", DashboardTheme.SmallFont, DashboardTheme.SecondaryTextColor, x + 730, y + 4);
    }
}
