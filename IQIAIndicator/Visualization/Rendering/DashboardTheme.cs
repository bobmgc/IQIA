using System.Drawing;
using OFT.Rendering.Tools;

namespace IQIAIndicator.Visualization.Rendering;

/// <summary>
/// Palette et polices partagées par tous les dashboards.
/// Source unique : évite la duplication de couleurs/polices qui existait
/// entre IQIAFusionDashboard et IQIAPipelineDashboard.
/// </summary>
internal static class DashboardTheme
{
    public static readonly RenderFont TitleFont = new("Arial", 13f);
    public static readonly RenderFont HeaderFont = new("Arial", 11f);
    public static readonly RenderFont BodyFont = new("Arial", 9.5f);
    public static readonly RenderFont SmallFont = new("Arial", 8.5f);

    public static readonly Color PanelBackground = Color.FromArgb(235, 28, 31, 35);
    public static readonly Color CardBackground = Color.FromArgb(235, 38, 42, 47);
    public static readonly Color BarBackground = Color.FromArgb(255, 65, 70, 76);
    public static readonly Color BorderColor = Color.FromArgb(255, 55, 60, 66);

    public static readonly Color TextColor = Color.White;
    public static readonly Color SecondaryTextColor = Color.LightGray;
    public static readonly Color MutedTextColor = Color.FromArgb(255, 140, 145, 150);

    public static readonly Color Green = Color.FromArgb(255, 57, 169, 85);
    public static readonly Color Orange = Color.FromArgb(255, 235, 151, 45);
    public static readonly Color Red = Color.FromArgb(255, 210, 68, 68);
    public static readonly Color Gray = Color.FromArgb(255, 140, 145, 150);
    public static readonly Color Accent = Color.FromArgb(255, 90, 155, 235);

    /// <summary>Vert/orange/rouge par seuil, cohérent avec l'ancien comportement des deux dashboards existants.</summary>
    public static Color StateColor(double value01) =>
        value01 >= 0.75 ? Green : value01 >= 0.40 ? Orange : Red;
}
