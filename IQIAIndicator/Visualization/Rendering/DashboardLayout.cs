namespace IQIAIndicator.Visualization.Rendering;

/// <summary>
/// Allocation des régions d'écran : empile les barres toujours visibles (System Health,
/// Replay Monitor, Scientific Collection Monitor) au-dessus du dashboard principal actif,
/// pour qu'ils ne se chevauchent jamais.
/// </summary>
internal static class DashboardLayout
{
    public const int OriginX = 12;
    public const int OriginY = 12;
    public const int BarWidth = 990;
    public const int BarHeight = 22;
    public const int BarSpacing = 5;

    public static int MainPanelY(bool showReplayMonitor, bool showCollectionMonitor)
    {
        int y = OriginY + BarHeight + BarSpacing;
        if (showReplayMonitor)
            y += BarHeight + BarSpacing;
        if (showCollectionMonitor)
            y += BarHeight + BarSpacing;

        return y;
    }
}
