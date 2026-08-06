using System.Drawing;
using System.Globalization;
using IQIAIndicator.Engine.Fusion.Core;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;

namespace IQIAIndicator.Visualization;

/// <summary>
/// Panneau ATAS en lecture seule pour observer les dimensions de fusion IQIA.
/// </summary>
internal sealed class IQIAFusionDashboard
{
    private const int PanelX = 12;
    private const int PanelY = 12;
    private const int PanelWidth = 310;
    private const int PanelHeight = 244;
    private const int BarWidth = 190;
    private const int RowHeight = 31;
    private const int FirstRowOffset = 76;

    private static readonly RenderFont HeaderFont = new("Arial", 12f);
    private static readonly RenderFont BodyFont = new("Arial", 10f);

    private static readonly Color PanelBackground = Color.FromArgb(235, 28, 31, 35);
    private static readonly Color BarBackground = Color.FromArgb(255, 65, 70, 76);
    private static readonly Color TextColor = Color.White;
    private static readonly Color SecondaryTextColor = Color.LightGray;
    private static readonly Color Green = Color.FromArgb(255, 57, 169, 85);
    private static readonly Color Orange = Color.FromArgb(255, 235, 151, 45);
    private static readonly Color Red = Color.FromArgb(255, 210, 68, 68);

    private static readonly DashboardRow[] Rows =
    [
        new(FusionDimension.Stationarity, "Stationarity"),
        new(FusionDimension.Persistence, "Persistence"),
        new(FusionDimension.MeanReversion, "MeanReversion"),
        new(FusionDimension.StructuralStability, "StructuralStability"),
        new(FusionDimension.RandomWalk, "RandomWalk")
    ];

    public void Draw(
        RenderContext renderContext,
        FusionResult fusionResult,
        int barIndex,
        DateTime timestamp,
        int availableEvidenceCount)
    {
        renderContext.FillRectangle(PanelBackground, new Rectangle(PanelX, PanelY, PanelWidth, PanelHeight));
        renderContext.DrawString("IQIA", HeaderFont, TextColor, PanelX + 10, PanelY + 8);
        renderContext.DrawString(
            $"Bar Index: {barIndex}  Heure: {timestamp:HH:mm:ss}",
            BodyFont,
            SecondaryTextColor,
            PanelX + 10,
            PanelY + 29);
        renderContext.DrawString(
            $"Évidences disponibles: {availableEvidenceCount}",
            BodyFont,
            SecondaryTextColor,
            PanelX + 10,
            PanelY + 46);

        for (int index = 0; index < Rows.Length; index++)
        {
            DashboardRow row = Rows[index];
            int rowY = PanelY + FirstRowOffset + index * RowHeight;
            double value = GetValue(fusionResult, row.Dimension);
            Color stateColor = GetStateColor(value);
            int filledWidth = (int)Math.Round(BarWidth * value, MidpointRounding.AwayFromZero);

            renderContext.DrawString(row.Label, BodyFont, TextColor, PanelX + 10, rowY);
            renderContext.DrawString(
                value.ToString("F2", CultureInfo.InvariantCulture),
                BodyFont,
                stateColor,
                PanelX + 240,
                rowY);

            var barBounds = new Rectangle(PanelX + 10, rowY + 15, BarWidth, 8);
            renderContext.FillRectangle(BarBackground, barBounds);
            renderContext.FillRectangle(stateColor, new Rectangle(barBounds.X, barBounds.Y, filledWidth, barBounds.Height));
        }
    }

    private static double GetValue(FusionResult fusionResult, FusionDimension dimension) =>
        fusionResult.Dimensions.TryGetValue(dimension, out FusionConfidence? confidence)
            ? Math.Clamp(confidence.Value, 0.0, 1.0)
            : 0.0;

    private static Color GetStateColor(double value) =>
        value >= 0.75 ? Green : value >= 0.40 ? Orange : Red;

    private readonly record struct DashboardRow(FusionDimension Dimension, string Label);
}