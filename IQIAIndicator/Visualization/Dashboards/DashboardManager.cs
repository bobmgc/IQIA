using IQIAIndicator.Visualization.Rendering;
using IQIAIndicator.Visualization.State;
using IQIAIndicator.Visualization.Widgets;
using OFT.Rendering.Context;

namespace IQIAIndicator.Visualization.Dashboards;

/// <summary>Dashboard principal choisi par l'utilisateur via la propriété ATAS "Dashboard actif".</summary>
public enum DashboardKind
{
    Trading,
    Scientific,
    Decision,
    Dataset,
    Performance,
    Debug
}

/// <summary>
/// Point d'entrée unique du système de dashboards. Décide uniquement QUEL dashboard afficher
/// et empile les barres toujours visibles (System Health, Replay Monitor, Scientific Collection
/// Monitor) au-dessus. Aucun dashboard ne connaît un autre dashboard : DashboardManager est le
/// seul composant qui les assemble.
/// </summary>
internal sealed class DashboardManager
{
    private readonly TradingDashboard _trading = new();
    private readonly ScientificDashboard _scientific = new();
    private readonly DecisionDashboard _decision = new();
    private readonly DatasetDashboard _dataset = new();
    private readonly PerformanceDashboard _performance = new();
    private readonly DebugDashboard _debug = new();

    public void Draw(
        RenderContext renderContext,
        DashboardContext context,
        DashboardKind activeDashboard,
        bool expandScientificDiagnostics)
    {
        SystemHealthReport health = SystemHealthAggregator.Evaluate(context);
        SystemHealthBar.Draw(renderContext, health, DashboardLayout.OriginX, DashboardLayout.OriginY, DashboardLayout.BarWidth);

        bool showReplay = context.Execution?.IsReplay == true;
        bool showCollection = context.EnableScientificDataset;

        int barY = DashboardLayout.OriginY + DashboardLayout.BarHeight + DashboardLayout.BarSpacing;

        if (showReplay && context.Execution is not null)
        {
            ReplayMonitorWidget.Draw(renderContext, context.Execution, DashboardLayout.OriginX, barY, DashboardLayout.BarWidth);
            barY += DashboardLayout.BarHeight + DashboardLayout.BarSpacing;
        }

        if (showCollection)
        {
            ScientificCollectionMonitorWidget.Draw(
                renderContext,
                context.DatasetCollector,
                context.DatasetSession,
                context.BarIndex,
                DashboardLayout.OriginX,
                barY,
                DashboardLayout.BarWidth);
            barY += DashboardLayout.BarHeight + DashboardLayout.BarSpacing;
        }

        int panelX = DashboardLayout.OriginX;
        int panelY = barY;

        switch (activeDashboard)
        {
            case DashboardKind.Trading:
                _trading.Draw(renderContext, context, panelX, panelY);
                break;
            case DashboardKind.Scientific:
                _scientific.Draw(renderContext, context, expandScientificDiagnostics, panelX, panelY);
                break;
            case DashboardKind.Decision:
                _decision.Draw(renderContext, context, panelX, panelY);
                break;
            case DashboardKind.Dataset:
                _dataset.Draw(renderContext, context, panelX, panelY);
                break;
            case DashboardKind.Performance:
                _performance.Draw(renderContext, context, panelX, panelY);
                break;
            case DashboardKind.Debug:
                _debug.Draw(renderContext, context, panelX, panelY);
                break;
        }
    }
}
