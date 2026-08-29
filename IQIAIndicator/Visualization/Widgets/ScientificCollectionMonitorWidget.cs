using System.Drawing;
using IQIAIndicator.Core.Calibration;
using IQIAIndicator.Visualization.Dashboards;
using IQIAIndicator.Visualization.Rendering;
using OFT.Rendering.Context;

namespace IQIAIndicator.Visualization.Widgets;

/// <summary>Visible en permanence quand EnableScientificDataset est activé.</summary>
internal static class ScientificCollectionMonitorWidget
{
    public static void Draw(
        RenderContext renderContext,
        ScientificDatasetCollector? collector,
        ExportResult? lastExportResult,
        bool enableScientificDataset,
        int currentBar,
        int x,
        int y,
        int width)
    {
        renderContext.FillRectangle(DashboardTheme.CardBackground, new Rectangle(x, y, width, DashboardLayout.BarHeight));

        // Sprint 15.17.1: status comes from the same resolver the Dataset dashboard uses
        // (DatasetDashboard.ResolveExportStatus) - never recomputed independently here.
        ExportStatus exportStatus = DatasetDashboard.ResolveExportStatus(enableScientificDataset, collector, lastExportResult);
        (string exportText, Color exportColor) = DescribeExportStatusCompact(exportStatus);

        renderContext.DrawString("COLLECTION", DashboardTheme.SmallFont, DashboardTheme.Accent, x + 8, y + 4);
        renderContext.DrawString(
            enableScientificDataset ? "Recording ON" : "Recording OFF",
            DashboardTheme.SmallFont,
            enableScientificDataset ? DashboardTheme.Green : DashboardTheme.Gray,
            x + 95,
            y + 4);
        renderContext.DrawString($"Current Bar {currentBar}", DashboardTheme.SmallFont, DashboardTheme.SecondaryTextColor, x + 210, y + 4);
        renderContext.DrawString($"Accepted {collector?.RecordsAccepted ?? 0}", DashboardTheme.SmallFont, DashboardTheme.Green, x + 380, y + 4);
        renderContext.DrawString($"Duplicates {collector?.DuplicateRecordsRejected ?? 0}", DashboardTheme.SmallFont, DashboardTheme.Orange, x + 490, y + 4);
        renderContext.DrawString($"Export {exportText}", DashboardTheme.SmallFont, exportColor, x + 630, y + 4);
    }

    /// <summary>Abbreviated presentation for this compact bar - "Pending"/"Failed"/"Exported"/
    /// "No Data" per Sprint 15.17.1's own wording. Same underlying ExportStatus as
    /// DatasetDashboard.DescribeExportStatus's full-word version - only the text differs.</summary>
    private static (string Text, Color Color) DescribeExportStatusCompact(ExportStatus status) => status switch
    {
        ExportStatus.NotEnabled => ("N/A", DashboardTheme.Gray),
        ExportStatus.NoData => ("No Data", DashboardTheme.Gray),
        ExportStatus.ReadyToExport => ("Pending", DashboardTheme.Orange),
        ExportStatus.Exported => ("Exported", DashboardTheme.Green),
        ExportStatus.ExportFailed => ("Failed", DashboardTheme.Red),
        _ => ("N/A", DashboardTheme.Gray)
    };
}
