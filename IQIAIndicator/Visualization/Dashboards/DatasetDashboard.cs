using System;
using System.Drawing;
using System.Linq;
using IQIAIndicator.Core.Calibration;
using IQIAIndicator.Visualization.Rendering;
using IQIAIndicator.Visualization.State;
using OFT.Rendering.Context;

namespace IQIAIndicator.Visualization.Dashboards;

/// <summary>
/// Centre de supervision de la collecte du dataset scientifique.
/// Toutes les valeurs proviennent de ScientificDatasetCollector/Session, déjà calculées
/// ailleurs — à l'exception de l'estimation RAM (approximation présentation, clairement
/// signalée, aucune mesure mémoire réelle n'étant instrumentée côté collecteur).
/// </summary>
internal sealed class DatasetDashboard
{
    public const int Width = 620;
    public const int Height = 560;
    private const double EstimatedBytesPerRecord = 512.0; // approximation grossière, à but indicatif uniquement

    // État propre à ce dashboard : ne pas recalculer les statistiques (corrélations/outliers)
    // à chaque frame — coûteux sur un gros dataset. Rafraîchi tous les 25 nouveaux records.
    private ScientificDatasetStatistics? _cachedStatistics;
    private int _cachedAtCount = -1;
    private double _peakEstimatedBytes;

    public void Draw(RenderContext renderContext, DashboardContext context, int x, int y)
    {
        renderContext.FillRectangle(DashboardTheme.PanelBackground, new Rectangle(x, y, Width, Height));
        DashboardCanvas.Title(renderContext, "IQIA — DATASET", x + 10, y + 8);

        if (!context.EnableScientificDataset)
        {
            renderContext.DrawString("Collecte désactivée (EnableScientificDataset = false).", DashboardTheme.BodyFont, DashboardTheme.SecondaryTextColor, x + 10, y + 44);
            return;
        }

        ScientificDatasetCollector? collector = context.DatasetCollector;
        int fieldY = y + 42;

        DashboardCanvas.SectionHeader(renderContext, "STATUT", x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Dataset Status", DatasetStatus(collector), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Session ID", collector?.SessionId.ToString() ?? "N/A", x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Replay Status", context.Execution?.IsReplay == true ? "Replay" : "Live/Historical", x + 10, ref fieldY);

        int expectedBars = Math.Max(context.Execution?.CurrentBar ?? 0, 1);
        int barsCollected = collector?.BarsCollected ?? 0;
        double progress = Math.Clamp((double)barsCollected / expectedBars, 0.0, 1.0);

        DashboardCanvas.Field(renderContext, "Bars Collected", DashboardCanvas.FormatInt(barsCollected), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Bars Expected", DashboardCanvas.FormatInt(expectedBars), x + 10, ref fieldY);

        TimeSpan elapsed = context.DatasetStartTime is { } start ? DateTime.UtcNow - start : TimeSpan.Zero;
        double msPerBar = barsCollected == 0 ? 0.0 : elapsed.TotalMilliseconds / barsCollected;
        TimeSpan remaining = TimeSpan.FromMilliseconds(msPerBar * Math.Max(expectedBars - barsCollected, 0));

        DashboardCanvas.Field(renderContext, "Elapsed Time", elapsed.ToString(@"hh\:mm\:ss"), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Estimated Remaining", remaining.ToString(@"hh\:mm\:ss"), x + 10, ref fieldY);

        fieldY += 4;
        DashboardCanvas.Gauge(renderContext, "Collection Progress", progress, x + 10, fieldY);
        fieldY += 36;

        DashboardCanvas.SectionHeader(renderContext, "INTÉGRITÉ", x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Accepted", DashboardCanvas.FormatInt(collector?.RecordsAccepted ?? 0), x + 10, ref fieldY, DashboardTheme.Green);
        DashboardCanvas.Field(renderContext, "Rejected / Duplicates", DashboardCanvas.FormatInt(collector?.DuplicateRecordsRejected ?? 0), x + 10, ref fieldY, (collector?.DuplicateRecordsRejected ?? 0) > 0 ? DashboardTheme.Orange : DashboardTheme.Green);
        DashboardCanvas.Field(renderContext, "Errors", DashboardCanvas.FormatInt(collector?.Errors ?? 0), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Integrity", collector is null ? "N/A" : "OK (invariant vérifié)", x + 10, ref fieldY, DashboardTheme.Green);
        DashboardCanvas.Field(renderContext, "Coverage", collector is null || collector.TotalAddAttempts == 0 ? "N/A" : DashboardCanvas.FormatPercent((double)collector.RecordsAccepted / collector.TotalAddAttempts), x + 10, ref fieldY);

        fieldY += 4;
        DashboardCanvas.SectionHeader(renderContext, "EXPORT", x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "CSV", context.DatasetSession?.CSVPath ?? "N/A", x + 10, ref fieldY, valueOffset: 90);
        DashboardCanvas.Field(renderContext, "JSON", context.DatasetSession?.JSONPath ?? "N/A", x + 10, ref fieldY, valueOffset: 90);
        DashboardCanvas.Field(renderContext, "Output Folder", DashboardCanvas.Truncate(context.DatasetOutputDirectory, 55), x + 10, ref fieldY, valueOffset: 90);
        DashboardCanvas.Field(renderContext, "Last Export", DashboardCanvas.FormatDate(context.DatasetSession?.EndTime), x + 10, ref fieldY, valueOffset: 90);
        DashboardCanvas.Field(renderContext, "Export Duration", context.DatasetSession is null ? "N/A" : $"{context.DatasetSession.Duration.TotalSeconds:F2} s", x + 10, ref fieldY, valueOffset: 90);
        DashboardCanvas.Field(renderContext, "Export Size", context.DatasetSession is null ? "N/A" : FormatBytes(context.DatasetSession.DatasetSizeBytes), x + 10, ref fieldY, valueOffset: 90);

        fieldY += 4;
        DashboardCanvas.SectionHeader(renderContext, "VITESSE", x + 10, ref fieldY);
        double barsPerSecond = elapsed.TotalSeconds <= 0 ? 0.0 : barsCollected / elapsed.TotalSeconds;
        DashboardCanvas.Field(renderContext, "Bars/sec", barsPerSecond.ToString("F2"), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Records/sec", barsPerSecond.ToString("F2"), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Average ms/bar", msPerBar.ToString("F2"), x + 10, ref fieldY);

        fieldY += 4;
        DashboardCanvas.SectionHeader(renderContext, "MÉMOIRE (estimation)", x + 10, ref fieldY);
        double estimatedBytes = (collector?.Count ?? 0) * EstimatedBytesPerRecord;
        _peakEstimatedBytes = Math.Max(_peakEstimatedBytes, estimatedBytes);
        DashboardCanvas.Field(renderContext, "Current Records", DashboardCanvas.FormatInt(collector?.Count ?? 0), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "RAM (est.)", FormatBytes((long)estimatedBytes), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Peak RAM (est.)", FormatBytes((long)_peakEstimatedBytes), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Collector Size (exporté)", context.DatasetSession is null ? "N/A" : FormatBytes(context.DatasetSession.DatasetSizeBytes), x + 10, ref fieldY, valueOffset: 145);

        fieldY += 4;
        DashboardCanvas.SectionHeader(renderContext, "CORRÉLATIONS", x + 10, ref fieldY);
        RefreshStatisticsIfNeeded(collector);
        if (_cachedStatistics is { Correlations.Count: > 0 })
        {
            foreach (var correlation in _cachedStatistics.Correlations.Take(5))
            {
                renderContext.DrawString(
                    $"{correlation.Left} × {correlation.Right}",
                    DashboardTheme.SmallFont,
                    DashboardTheme.SecondaryTextColor,
                    x + 10,
                    fieldY);
                renderContext.DrawString(
                    $"r={correlation.Correlation:F3} ({correlation.Strength})",
                    DashboardTheme.SmallFont,
                    DashboardTheme.TextColor,
                    x + 350,
                    fieldY);
                fieldY += 13;
            }
        }
        else
        {
            renderContext.DrawString("Pas encore assez de données.", DashboardTheme.SmallFont, DashboardTheme.SecondaryTextColor, x + 10, fieldY);
            fieldY += 13;
        }

        fieldY += 4;
        DashboardCanvas.SectionHeader(renderContext, "DIAGNOSTICS", x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Dernière erreur", string.IsNullOrWhiteSpace(collector?.RejectedReason) ? "Aucune" : collector.RejectedReason, x + 10, ref fieldY, valueOffset: 130);
    }

    private static string DatasetStatus(ScientificDatasetCollector? collector)
    {
        if (collector is null || collector.TotalAddAttempts == 0)
            return "Idle";

        // "Exporting" n'est pas distinguable de "Recording" : l'export est synchrone et
        // ne laisse pas d'état intermédiaire observable dans ScientificDatasetCollector.
        return "Recording";
    }

    private void RefreshStatisticsIfNeeded(ScientificDatasetCollector? collector)
    {
        if (collector is null || collector.Count == 0)
            return;

        if (_cachedStatistics is not null && collector.Count - _cachedAtCount < 25)
            return;

        _cachedStatistics = collector.Describe();
        _cachedAtCount = collector.Count;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";

        double kb = bytes / 1024.0;
        if (kb < 1024)
            return $"{kb:F1} KB";

        return $"{kb / 1024.0:F2} MB";
    }
}
