using System.Drawing;
using System.Globalization;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Regime.Core;
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
    private const int DebugPanelWidth = 1050;
    private const int DebugPanelHeight = 565;
    private const int BarWidth = 190;
    private const int RowHeight = 31;
    private const int FirstRowOffset = 76;
    private const int DebugLineHeight = 16;

    private static readonly RenderFont HeaderFont = new("Arial", 12f);
    private static readonly RenderFont BodyFont = new("Arial", 10f);
    private static readonly RenderFont DebugFont = new("Arial", 9f);

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
        EvidenceSet evidence,
        FusionResult fusionResult,
        int barIndex,
        DateTime timestamp,
        int availableEvidenceCount,
        bool debugMode)
    {
        if (debugMode)
        {
            DrawDebug(renderContext, evidence, fusionResult, barIndex, timestamp, availableEvidenceCount);
            return;
        }

        DrawSummary(renderContext, fusionResult, barIndex, timestamp, availableEvidenceCount);
    }

    private static void DrawSummary(
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

    private static void DrawDebug(
        RenderContext renderContext,
        EvidenceSet evidence,
        FusionResult fusionResult,
        int barIndex,
        DateTime timestamp,
        int availableEvidenceCount)
    {
        renderContext.FillRectangle(PanelBackground, new Rectangle(PanelX, PanelY, DebugPanelWidth, DebugPanelHeight));
        renderContext.DrawString("IQIA - Mode Debug", HeaderFont, TextColor, PanelX + 10, PanelY + 8);
        renderContext.DrawString(
            $"Bar Index: {barIndex}  Heure: {timestamp:HH:mm:ss}  Évidences disponibles: {availableEvidenceCount}",
            BodyFont,
            SecondaryTextColor,
            PanelX + 10,
            PanelY + 29);

        int evidenceY = PanelY + 58;
        DrawSectionTitle(renderContext, "Evidence Models", PanelX + 10, evidenceY);
        DrawEvidenceModels(renderContext, evidence, PanelX + 10, evidenceY + 20);

        int rulesY = PanelY + 225;
        DrawSectionTitle(renderContext, "Fusion Rules", PanelX + 10, rulesY);
        DrawFusionRules(renderContext, fusionResult, PanelX + 10, rulesY + 20);

        int resultY = PanelY + 375;
        DrawSectionTitle(renderContext, "Fusion Result", PanelX + 10, resultY);
        DrawFusionResult(renderContext, fusionResult, PanelX + 10, resultY + 22);
    }

    private static void DrawEvidenceModels(RenderContext renderContext, EvidenceSet evidence, int x, int y)
    {
        DrawDebugLine(renderContext, x, y, "ADF", FormatAdf(evidence));
        DrawDebugLine(renderContext, x, y + DebugLineHeight, "KPSS", FormatKpss(evidence));
        DrawDebugLine(renderContext, x, y + 2 * DebugLineHeight, "DFA", FormatDfa(evidence));
        DrawDebugLine(renderContext, x, y + 3 * DebugLineHeight, "Half-Life", FormatHalfLife(evidence));
        DrawDebugLine(renderContext, x, y + 4 * DebugLineHeight, "Variance Ratio", FormatVarianceRatio(evidence));
        DrawDebugLine(renderContext, x, y + 5 * DebugLineHeight, "CUSUM", FormatCusum(evidence));
        DrawDebugLine(renderContext, x, y + 6 * DebugLineHeight, "Bai-Perron", FormatBaiPerron(evidence));
    }

    private static void DrawFusionRules(RenderContext renderContext, FusionResult fusionResult, int x, int y)
    {
        for (int index = 0; index < Rows.Length; index++)
        {
            DashboardRow row = Rows[index];
            FusionConfidence confidence = GetConfidence(fusionResult, row.Dimension);
            int rowY = y + index * 25;

            renderContext.DrawString($"{row.Label}Rule", DebugFont, TextColor, x, rowY);
            renderContext.DrawString(
                $"Value={confidence.Value:F3}  Explanation={Truncate(confidence.Explanation, 112)}",
                DebugFont,
                GetStateColor(confidence.Value),
                x + 190,
                rowY);
        }
    }

    private static void DrawFusionResult(RenderContext renderContext, FusionResult fusionResult, int x, int y)
    {
        const int debugBarWidth = 250;
        const int debugRowHeight = 29;

        for (int index = 0; index < Rows.Length; index++)
        {
            DashboardRow row = Rows[index];
            int rowY = y + index * debugRowHeight;
            double value = GetValue(fusionResult, row.Dimension);
            Color stateColor = GetStateColor(value);
            int filledWidth = (int)Math.Round(debugBarWidth * value, MidpointRounding.AwayFromZero);

            renderContext.DrawString(row.Label, BodyFont, TextColor, x, rowY);
            renderContext.DrawString(value.ToString("F2", CultureInfo.InvariantCulture), BodyFont, stateColor, x + 180, rowY);
            var barBounds = new Rectangle(x + 235, rowY + 2, debugBarWidth, 11);
            renderContext.FillRectangle(BarBackground, barBounds);
            renderContext.FillRectangle(stateColor, new Rectangle(barBounds.X, barBounds.Y, filledWidth, barBounds.Height));
        }
    }

    private static void DrawSectionTitle(RenderContext renderContext, string title, int x, int y) =>
        renderContext.DrawString($"================ {title} ================", BodyFont, TextColor, x, y);

    private static void DrawDebugLine(RenderContext renderContext, int x, int y, string model, string values)
    {
        renderContext.DrawString(model, DebugFont, TextColor, x, y);
        renderContext.DrawString(values, DebugFont, SecondaryTextColor, x + 120, y);
    }

    private static string FormatAdf(EvidenceSet evidence) => evidence.Adf is { } adf
        ? $"Statistic={adf.Statistic:F3}  PValue={adf.PValue:F3}  Confidence={adf.Confidence:F3}  IsStationary={adf.IsStationary}"
        : "Indisponible";

    private static string FormatKpss(EvidenceSet evidence) => evidence.Kpss is { } kpss
        ? $"Statistic={kpss.Statistic:F3}  PValue={kpss.PValue:F3}  Confidence={kpss.Confidence:F3}  IsStationary={kpss.IsStationary}"
        : "Indisponible";

    private static string FormatDfa(EvidenceSet evidence) => evidence.Dfa is { } dfa
        ? $"Hurst={dfa.Hurst:F3}  RSquared={dfa.RSquared:F3}  Confidence={dfa.Confidence:F3}  WindowCount={dfa.WindowCount}"
        : "Indisponible";

    private static string FormatHalfLife(EvidenceSet evidence) => evidence.HalfLife is { } halfLife
        ? $"HalfLife={halfLife.HalfLife:F3}  RSquared={halfLife.RSquared:F3}  Confidence={halfLife.Confidence:F3}  SampleSize={halfLife.SampleSize}"
        : "Indisponible";

    private static string FormatVarianceRatio(EvidenceSet evidence) => evidence.VarianceRatio is { } varianceRatio
        ? $"VarianceRatio={varianceRatio.VarianceRatio:F3}  ZScore={varianceRatio.ZStatistic:F3}  PValue={varianceRatio.PValue:F3}  Confidence={varianceRatio.Confidence:F3}"
        : "Indisponible";

    private static string FormatCusum(EvidenceSet evidence) => evidence.Cusum is { } cusum
        ? $"Positive={cusum.PositiveCusum:F3}  Negative={cusum.NegativeCusum:F3}  Threshold={cusum.Threshold:F3}  Confidence={cusum.Confidence:F3}"
        : "Indisponible";

    private static string FormatBaiPerron(EvidenceSet evidence) => evidence.BaiPerron is { } baiPerron
        ? $"BreakCount={baiPerron.BreakCount}  Confidence={baiPerron.Confidence:F3}  BIC={baiPerron.BicScore:F3}  SampleSize={baiPerron.SampleSize}"
        : "Indisponible";

    private static double GetValue(FusionResult fusionResult, FusionDimension dimension) =>
        Math.Clamp(GetConfidence(fusionResult, dimension).Value, 0.0, 1.0);

    private static FusionConfidence GetConfidence(FusionResult fusionResult, FusionDimension dimension) =>
        fusionResult.Dimensions.TryGetValue(dimension, out FusionConfidence? confidence)
            ? confidence
            : new FusionConfidence
            {
                Value = 0.0,
                Explanation = "Indisponible"
            };

    private static Color GetStateColor(double value) =>
        value >= 0.75 ? Green : value >= 0.40 ? Orange : Red;

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..(maximumLength - 3)] + "...";

    private readonly record struct DashboardRow(FusionDimension Dimension, string Label);
}