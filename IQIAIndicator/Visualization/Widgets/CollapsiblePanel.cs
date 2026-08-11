using OFT.Rendering.Context;
using IQIAIndicator.Visualization.Rendering;

namespace IQIAIndicator.Visualization.Widgets;

/// <summary>
/// En-tête de section repliable. L'état "expanded" est fourni par l'appelant (typiquement
/// une propriété ATAS du dashboard, ex. "Afficher les diagnostics détaillés") : ATAS n'offre
/// pas de zone cliquable native sur le canevas de dessin, donc le repli/dépli se pilote via
/// la grille de propriétés de l'indicateur plutôt que par un clic sur le graphique.
/// Par défaut (non déplié), seul l'en-tête est dessiné : les listes de diagnostics restent
/// masquées tant que l'utilisateur n'active pas l'option correspondante.
/// </summary>
internal static class CollapsiblePanel
{
    public static void Header(RenderContext renderContext, string title, bool expanded, int x, ref int y)
    {
        string caret = expanded ? "▾" : "▸";
        renderContext.DrawString($"{caret} {title}", DashboardTheme.SmallFont, DashboardTheme.SecondaryTextColor, x, y);
        y += 14;
    }
}
