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
    // Height couvre le pire cas réel du contenu (STATUT..DIAGNOSTICS avec 5 corrélations) :
    // ~709px mesurés, + marge de sécurité — cf. audit C1 (le panneau doit contenir son propre contenu).
    // Sprint 15.17 : +7 lignes (Bars Received / Rejected-Invalid / Out Of Order / First Timestamp /
    // Last Timestamp / OHLCV CSV / Metadata, 16px chacune, cf. DashboardCanvas.Field) => ~821px,
    // marge conservée proportionnelle à l'original (51px sur 709px).
    // Sprint 15.17.1 : +3 lignes EXPORT (Export Status / Record Count / Last Export Error) + 1
    // SectionHeader (18px) + 5 lignes LIFECYCLE (Constructed / Dataset Enabled / First Add /
    // Last Add / OnDispose Entered) => +146px => ~967px, marge conservée.
    // Sprint 15.17.2 : +1 ligne "Lifecycle File" toujours affichée, +1 ligne "Lifecycle Write Error"
    // affichée uniquement en cas d'échec d'écriture (pire cas +32px) => ~999px, marge conservée.
    public const int Height = 1050;
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

        ScientificDatasetCollector? collector = context.DatasetCollector;
        DatasetState state = ResolveDatasetStatus(collector, context.EnableScientificDataset);

        // Court-circuit uniquement si la collecte est désactivée ET qu'il n'existe réellement rien
        // à montrer (état Idle). Si l'état résolu est Stopped, le collecteur détient encore de vraies
        // données historiques : on continue d'afficher le panneau complet plutôt que de les masquer
        // derrière ce message.
        if (!context.EnableScientificDataset && state != DatasetState.Stopped)
        {
            renderContext.DrawString("Collecte désactivée (EnableScientificDataset = false).", DashboardTheme.BodyFont, DashboardTheme.SecondaryTextColor, x + 10, y + 44);
            return;
        }

        int fieldY = y + 42;

        DashboardCanvas.SectionHeader(renderContext, "STATUT", x + 10, ref fieldY);
        (string statusText, Color statusColor) = DescribeState(state);
        DashboardCanvas.Field(renderContext, "Dataset Status", statusText, x + 10, ref fieldY, statusColor);
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
        // Sprint 15.17 : "Bars Received" = TotalAddAttempts (chaque tentative d'Add, acceptée ou non) -
        // distinct de "Accepted" (RecordsAccepted, réellement écrites dans le dataset).
        DashboardCanvas.Field(renderContext, "Bars Received", DashboardCanvas.FormatInt(collector?.TotalAddAttempts ?? 0), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "Accepted", DashboardCanvas.FormatInt(collector?.RecordsAccepted ?? 0), x + 10, ref fieldY, DashboardTheme.Green);
        DashboardCanvas.Field(renderContext, "Rejected / Duplicates", DashboardCanvas.FormatInt(collector?.DuplicateRecordsRejected ?? 0), x + 10, ref fieldY, (collector?.DuplicateRecordsRejected ?? 0) > 0 ? DashboardTheme.Orange : DashboardTheme.Green);
        // Sprint 15.17 : compteur séparé pour les barres rejetées pour invalidité structurelle OHLCV
        // (High<Low, etc. - ScientificDatasetCollector.TryValidateOhlcv), distinct des doublons.
        DashboardCanvas.Field(renderContext, "Rejected / Invalid", DashboardCanvas.FormatInt(collector?.InvalidRecordsRejected ?? 0), x + 10, ref fieldY, (collector?.InvalidRecordsRejected ?? 0) > 0 ? DashboardTheme.Orange : DashboardTheme.Green);
        // Sprint 15.17 : accepté mais avec un timestamp antérieur au maximum déjà vu (cf. doc
        // ScientificDatasetCollector - non rejeté, uniquement compté : voir rapport Sprint 15.17 §6).
        DashboardCanvas.Field(renderContext, "Out Of Order", DashboardCanvas.FormatInt(collector?.RecordsOutOfOrder ?? 0), x + 10, ref fieldY, (collector?.RecordsOutOfOrder ?? 0) > 0 ? DashboardTheme.Orange : DashboardTheme.Green);
        // Errors = DuplicateRecordsRejected + InvalidRecordsRejected (ScientificDatasetCollector.cs) -
        // les deux compteurs détaillés sont maintenant affichés séparément ci-dessus.
        DashboardCanvas.Field(renderContext, "Errors", DashboardCanvas.FormatInt(collector?.Errors ?? 0), x + 10, ref fieldY, (collector?.Errors ?? 0) > 0 ? DashboardTheme.Orange : DashboardTheme.Green);
        DashboardCanvas.Field(renderContext, "Integrity", collector is null ? "N/A" : "OK (invariant vérifié)", x + 10, ref fieldY, DashboardTheme.Green);
        DashboardCanvas.Field(renderContext, "Coverage", collector is null || collector.TotalAddAttempts == 0 ? "N/A" : DashboardCanvas.FormatPercent((double)collector.RecordsAccepted / collector.TotalAddAttempts), x + 10, ref fieldY);
        DashboardCanvas.Field(renderContext, "First Timestamp", collector?.FirstTimestamp?.ToString("O") ?? "N/A", x + 10, ref fieldY, valueOffset: 130);
        DashboardCanvas.Field(renderContext, "Last Timestamp", collector?.LastTimestamp?.ToString("O") ?? "N/A", x + 10, ref fieldY, valueOffset: 130);

        fieldY += 4;
        DashboardCanvas.SectionHeader(renderContext, "EXPORT", x + 10, ref fieldY);
        // Sprint 15.17.1 : statut réel de l'export, dérivé du même résolveur que le widget compact
        // (ResolveExportStatus) - jamais recalculé indépendamment ici.
        ExportStatus exportStatus = ResolveExportStatus(context.EnableScientificDataset, collector, context.LastExportResult);
        (string exportStatusText, Color exportStatusColor) = DescribeExportStatus(exportStatus);
        DashboardCanvas.Field(renderContext, "Export Status", exportStatusText, x + 10, ref fieldY, exportStatusColor);
        // Sprint 15.17.1 : chemin RÉELLEMENT utilisé par cette instance (context.DatasetOutputDirectory
        // vient directement de IQIAIndicator.ScientificDatasetOutputDirectory) - jamais reconstruit ici.
        DashboardCanvas.Field(renderContext, "Output Directory", DashboardCanvas.Truncate(context.DatasetOutputDirectory, 55), x + 10, ref fieldY, valueOffset: 105);
        DashboardCanvas.Field(renderContext, "Record Count", DashboardCanvas.FormatInt(context.LastExportResult?.RecordCount ?? 0), x + 10, ref fieldY, valueOffset: 105);
        DashboardCanvas.Field(renderContext, "CSV", context.LastExportResult?.CsvPath ?? "N/A", x + 10, ref fieldY, valueOffset: 105);
        DashboardCanvas.Field(renderContext, "JSON", context.LastExportResult?.JsonPath ?? "N/A", x + 10, ref fieldY, valueOffset: 105);
        DashboardCanvas.Field(renderContext, "OHLCV CSV", context.LastExportResult?.OhlcvCsvPath ?? "N/A", x + 10, ref fieldY, valueOffset: 105);
        DashboardCanvas.Field(renderContext, "Metadata", context.LastExportResult?.MetadataPath ?? "N/A", x + 10, ref fieldY, valueOffset: 105);
        DashboardCanvas.Field(renderContext, "Last Export Time", DashboardCanvas.FormatDate(context.LastExportResult?.ExportedAt), x + 10, ref fieldY, valueOffset: 105);
        DashboardCanvas.Field(renderContext, "Export Duration", context.DatasetSession is null ? "N/A" : $"{context.DatasetSession.Duration.TotalSeconds:F2} s", x + 10, ref fieldY, valueOffset: 105);
        DashboardCanvas.Field(renderContext, "Export Size", context.DatasetSession is null ? "N/A" : FormatBytes(context.DatasetSession.DatasetSizeBytes), x + 10, ref fieldY, valueOffset: 105);
        // Sprint 15.17.1 : ce champ ne montre plus jamais "Pending" pour un échec réel - la distinction
        // NoData/ReadyToExport (jamais tenté) vs ExportFailed (tenté, a échoué) vient d'ExportStatus
        // ci-dessus ; ce champ porte uniquement le message d'erreur réel, le cas échéant.
        DashboardCanvas.Field(
            renderContext,
            "Last Export Error",
            exportStatus == ExportStatus.ExportFailed ? (context.LastExportResult?.ErrorMessage ?? "N/A") : "Aucune",
            x + 10,
            ref fieldY,
            exportStatus == ExportStatus.ExportFailed ? DashboardTheme.Red : DashboardTheme.Green,
            valueOffset: 130);

        fieldY += 4;
        DashboardCanvas.SectionHeader(renderContext, "LIFECYCLE (diagnostic ATAS)", x + 10, ref fieldY);
        // Sprint 15.17.1 : répond à une seule question - ATAS appelle-t-il réellement OnDispose() au
        // retrait de l'indicateur ? Après un vrai retrait, "OnDispose Entered" doit être renseigné et
        // postérieur à "Last Add" pour le confirmer. Voir DatasetLifecycleLog.
        DatasetLifecycleLog? lifecycle = context.DatasetLifecycleLog;
        DashboardCanvas.Field(renderContext, "Constructed", DashboardCanvas.FormatDate(lifecycle?.ConstructedAt), x + 10, ref fieldY, valueOffset: 150);
        DashboardCanvas.Field(renderContext, "Dataset Enabled", DashboardCanvas.FormatDate(lifecycle?.DatasetEnabledAt), x + 10, ref fieldY, valueOffset: 150);
        DashboardCanvas.Field(renderContext, "First Add", DashboardCanvas.FormatDate(lifecycle?.FirstAddAt), x + 10, ref fieldY, valueOffset: 150);
        DashboardCanvas.Field(renderContext, "Last Add", DashboardCanvas.FormatDate(lifecycle?.LastAddAt), x + 10, ref fieldY, valueOffset: 150);
        DashboardCanvas.Field(
            renderContext,
            "OnDispose Entered",
            DashboardCanvas.FormatDate(lifecycle?.OnDisposeEnteredAt),
            x + 10,
            ref fieldY,
            lifecycle?.OnDisposeEnteredAt is null ? DashboardTheme.Gray : DashboardTheme.Green,
            valueOffset: 150);
        // Sprint 15.17.2: the dashboard itself disappears the moment the indicator is removed from
        // the chart - this path is the actual source of truth AFTER that point, per the sprint's own
        // brief ("Le Dashboard ne doit PAS devenir la source de vérité"). Shown here only so a user
        // watching the dashboard while the indicator is still attached knows exactly where to look
        // afterward, in Windows Explorer, with no terminal required.
        DashboardCanvas.Field(renderContext, "Lifecycle File", DashboardCanvas.Truncate(context.LifecycleSnapshotPath, 55), x + 10, ref fieldY, valueOffset: 150);
        if (!string.IsNullOrWhiteSpace(context.LifecycleSnapshotError))
        {
            DashboardCanvas.Field(renderContext, "Lifecycle Write Error", context.LifecycleSnapshotError, x + 10, ref fieldY, DashboardTheme.Red, valueOffset: 150);
        }

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
        // Sprint 15.17.1 : renommé pour ne plus être confondu avec "Last Export Error" (section
        // EXPORT ci-dessus) - ceci est le dernier rejet au niveau d'UNE BARRE (doublon/OHLCV invalide),
        // pas un échec d'export.
        DashboardCanvas.Field(renderContext, "Dernier rejet (barre)", string.IsNullOrWhiteSpace(collector?.RejectedReason) ? "Aucun" : collector.RejectedReason, x + 10, ref fieldY, valueOffset: 150);
    }

    /// <summary>
    /// Résout l'état honnête du Dataset à partir des seules données réellement exposées par
    /// ScientificDatasetCollector.Status et DashboardContext.EnableScientificDataset. Aucun état
    /// inventé, aucune state machine nouvelle : Exporting/Error restent hors périmètre car non
    /// observables (cf. Sprint 13.2/13.3).
    ///
    /// Collecting/Debug ne sont retenus que si la collecte est actuellement activée : le Collector
    /// ne connaît pas EnableScientificDataset, donc Collector.Status resterait "Collecting" même
    /// après un arrêt (TotalAddAttempts ne redescend jamais à 0). Stopped n'est retenu que si des
    /// données ont réellement été collectées avant l'arrêt — jamais sur une instance qui n'a jamais
    /// collecté (dans ce cas : Idle).
    /// </summary>
    internal static DatasetState ResolveDatasetStatus(ScientificDatasetCollector? collector, bool enableScientificDataset)
    {
        if (collector is null)
            return DatasetState.Idle;

        if (enableScientificDataset && collector.Status == "Collecting")
            return DatasetState.Collecting;

        if (enableScientificDataset && collector.Status == "Debug")
            return DatasetState.Debug;

        if (collector.TotalAddAttempts > 0 && !enableScientificDataset)
            return DatasetState.Stopped;

        return DatasetState.Idle;
    }

    /// <summary>Wording/couleur réutilisant la palette existante : Green = actif (comme "Accepted"),
    /// Orange = diagnostic (comme les autres signaux Warn), Gray = neutre/inactif. Aucune nouvelle
    /// couleur introduite.</summary>
    private static (string Text, Color Color) DescribeState(DatasetState state) => state switch
    {
        DatasetState.Idle => ("IDLE", DashboardTheme.Gray),
        DatasetState.Collecting => ("COLLECTING", DashboardTheme.Green),
        DatasetState.Debug => ("DEBUG", DashboardTheme.Orange),
        DatasetState.Stopped => ("STOPPED", DashboardTheme.Gray),
        _ => ("N/A", DashboardTheme.Gray)
    };

    /// <summary>
    /// Sprint 15.17.1: resolves the honest EXPORT status - deliberately a SEPARATE, smaller state
    /// machine from DatasetState above rather than an extension of it. DatasetState answers "is
    /// collection happening"; this answers "did the last export attempt succeed" - two orthogonal
    /// questions (you can be Collecting with ExportStatus=ReadyToExport, or Stopped with
    /// ExportStatus=Exported from an earlier manual export, etc.). Cramming both into one enum would
    /// either lose information or require states for combinations that can't happen, which is worse
    /// than two small, single-purpose enums. Reused as-is by ScientificCollectionMonitorWidget so the
    /// computation is never duplicated - only DescribeExportStatus's presentation text/color differs
    /// per call site.
    ///
    /// A previously-successful export is reported as Exported even if more bars have since been
    /// collected (i.e. this does not mean "everything currently in memory is on disk", only "the
    /// last attempted export succeeded") - OnDispose's auto-export guard means this only matters for
    /// manual re-export scenarios, and is called out explicitly here rather than silently assumed.
    /// </summary>
    internal static ExportStatus ResolveExportStatus(bool enableScientificDataset, ScientificDatasetCollector? collector, ExportResult? lastExportResult)
    {
        if (!enableScientificDataset)
            return ExportStatus.NotEnabled;

        if (lastExportResult is not null)
        {
            return lastExportResult.Outcome switch
            {
                ExportOutcome.Success => ExportStatus.Exported,
                ExportOutcome.Failed => ExportStatus.ExportFailed,
                _ => ExportStatus.NoData
            };
        }

        return collector is null || collector.Count == 0 ? ExportStatus.NoData : ExportStatus.ReadyToExport;
    }

    /// <summary>Full-word presentation for the Dataset panel. See
    /// ScientificCollectionMonitorWidget.DescribeExportStatusCompact for the abbreviated widget
    /// wording - same underlying ExportStatus, different presentation only.</summary>
    private static (string Text, Color Color) DescribeExportStatus(ExportStatus status) => status switch
    {
        ExportStatus.NotEnabled => ("NOT ENABLED", DashboardTheme.Gray),
        ExportStatus.NoData => ("NO DATA", DashboardTheme.Gray),
        ExportStatus.ReadyToExport => ("READY TO EXPORT", DashboardTheme.Orange),
        ExportStatus.Exported => ("EXPORTED", DashboardTheme.Green),
        ExportStatus.ExportFailed => ("EXPORT FAILED", DashboardTheme.Red),
        _ => ("N/A", DashboardTheme.Gray)
    };

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

/// <summary>État Dataset honnête affiché par le Dashboard, dérivé uniquement de
/// ScientificDatasetCollector.Status et DashboardContext.EnableScientificDataset. Exporting/Error
/// ne sont pas modélisés ici : ils ne sont pas observables avec l'infrastructure actuelle
/// (cf. Sprint 13.2 — nécessite une évolution de ScientificDatasetSessionWriter).</summary>
internal enum DatasetState
{
    Idle,
    Collecting,
    Debug,
    Stopped
}

/// <summary>Sprint 15.17.1: honest EXPORT status, deliberately separate from DatasetState above -
/// see DatasetDashboard.ResolveExportStatus's doc comment for why these are two enums, not one.</summary>
internal enum ExportStatus
{
    NotEnabled,
    NoData,
    ReadyToExport,
    Exported,
    ExportFailed
}
