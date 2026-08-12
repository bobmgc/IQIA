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

    /// <param name="visibleChartWidth">
    /// Largeur réelle de la zone de chart visible (ATAS Indicator.ChartArea.Width), ou 0/négatif
    /// si pas encore disponible. Sert uniquement à remplacer un débordement silencieux sur les
    /// chandeliers par un message honnête — aucun panneau n'est repositionné ni redimensionné.
    /// </param>
    public void Draw(
        RenderContext renderContext,
        DashboardContext context,
        DashboardKind activeDashboard,
        int visibleChartWidth)
    {
        SystemHealthReport health = SystemHealthAggregator.Evaluate(context);

        int barsRequiredWidth = DashboardLayout.OriginX + DashboardLayout.BarWidth;
        if (Fits(visibleChartWidth, barsRequiredWidth))
        {
            SystemHealthBar.Draw(renderContext, health, DashboardLayout.OriginX, DashboardLayout.OriginY, DashboardLayout.BarWidth);
        }
        else
        {
            DashboardCanvas.WidthWarning(renderContext, "System Health", barsRequiredWidth, visibleChartWidth, DashboardLayout.OriginX, DashboardLayout.OriginY);
        }

        bool showReplay = context.Execution?.IsReplay == true;
        bool showCollection = context.EnableScientificDataset;

        int barY = DashboardLayout.OriginY + DashboardLayout.BarHeight + DashboardLayout.BarSpacing;

        if (showReplay && context.Execution is not null)
        {
            if (Fits(visibleChartWidth, barsRequiredWidth))
                ReplayMonitorWidget.Draw(renderContext, context.Execution, DashboardLayout.OriginX, barY, DashboardLayout.BarWidth);
            else
                DashboardCanvas.WidthWarning(renderContext, "Replay Monitor", barsRequiredWidth, visibleChartWidth, DashboardLayout.OriginX, barY);

            barY += DashboardLayout.BarHeight + DashboardLayout.BarSpacing;
        }

        if (showCollection)
        {
            if (Fits(visibleChartWidth, barsRequiredWidth))
            {
                ScientificCollectionMonitorWidget.Draw(
                    renderContext,
                    context.DatasetCollector,
                    context.DatasetSession,
                    context.BarIndex,
                    DashboardLayout.OriginX,
                    barY,
                    DashboardLayout.BarWidth);
            }
            else
            {
                DashboardCanvas.WidthWarning(renderContext, "Scientific Collection", barsRequiredWidth, visibleChartWidth, DashboardLayout.OriginX, barY);
            }

            barY += DashboardLayout.BarHeight + DashboardLayout.BarSpacing;
        }

        int panelX = DashboardLayout.OriginX;
        int panelY = barY;

        int panelWidth = activeDashboard switch
        {
            DashboardKind.Trading => TradingDashboard.Width,
            DashboardKind.Scientific => ScientificDashboard.Width,
            DashboardKind.Decision => DecisionDashboard.Width,
            DashboardKind.Dataset => DatasetDashboard.Width,
            DashboardKind.Performance => PerformanceDashboard.Width,
            DashboardKind.Debug => DebugDashboard.Width,
            _ => 0
        };
        int panelRequiredWidth = panelX + panelWidth;

        if (!Fits(visibleChartWidth, panelRequiredWidth))
        {
            DashboardCanvas.WidthWarning(renderContext, activeDashboard.ToString(), panelRequiredWidth, visibleChartWidth, panelX, panelY);
            return;
        }

        int panelHeight = activeDashboard switch
        {
            DashboardKind.Trading => TradingDashboard.Height,
            DashboardKind.Scientific => ScientificDashboard.Height,
            DashboardKind.Decision => DecisionDashboard.Height,
            DashboardKind.Dataset => DatasetDashboard.Height,
            DashboardKind.Performance => PerformanceDashboard.Height,
            DashboardKind.Debug => DebugDashboard.Height,
            _ => 0
        };

        switch (activeDashboard)
        {
            case DashboardKind.Trading:
                _trading.Draw(renderContext, context, panelX, panelY);
                break;
            case DashboardKind.Scientific:
                _scientific.Draw(renderContext, context, panelX, panelY);
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

        // H7 : rappel discret que d'autres vues existent (aucune découvrabilité native sur le
        // canevas ATAS — la seule vraie commande est la propriété "Dashboard actif").
        renderContext.DrawString(
            "5 autres vues disponibles via la propriété \"Dashboard actif\"",
            DashboardTheme.SmallFont,
            DashboardTheme.MutedTextColor,
            panelX,
            panelY + panelHeight + 4);
    }

    /// <summary>Vrai si la largeur visible est inconnue (&lt;= 0, ChartArea pas encore disponible —
    /// comportement historique conservé) ou suffisante pour le contenu requis.</summary>
    private static bool Fits(int visibleWidth, int requiredWidth) =>
        visibleWidth <= 0 || visibleWidth >= requiredWidth;

    /// <summary>
    /// Point d'entrée unique pour IQIAIndicator.ProcessMouseClick (Sprint 13.5, H2). Ne connaît
    /// aucun calcul scientifique : transmet uniquement le clic au Dashboard actif, qui seul sait
    /// résoudre ses propres zones cliquables (Étape 8). Un clic n'affecte jamais un Dashboard non
    /// actif (Étape 13) — seul Scientific expose des zones cliquables aujourd'hui ; les autres
    /// dashboards renvoient toujours "non consommé", sans erreur ni effet de bord.
    /// </summary>
    public bool TryHandleMouseClick(int pointX, int pointY, DashboardKind activeDashboard) =>
        activeDashboard switch
        {
            DashboardKind.Scientific => _scientific.TryHandleClick(pointX, pointY),
            _ => false
        };
}
