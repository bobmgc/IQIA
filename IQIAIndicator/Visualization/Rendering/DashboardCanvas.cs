using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using OFT.Rendering.Context;

namespace IQIAIndicator.Visualization.Rendering;

/// <summary>
/// Primitives de dessin partagées par tous les dashboards : panneaux, titres, champs,
/// badges de statut, barres, jauges et helpers de formatage.
/// Ne calcule rien : dessine uniquement ce qu'on lui passe.
/// </summary>
internal static class DashboardCanvas
{
    public static void Panel(RenderContext renderContext, int x, int y, int width, int height, Color? background = null) =>
        renderContext.FillRectangle(background ?? DashboardTheme.PanelBackground, new Rectangle(x, y, width, height));

    public static void Title(RenderContext renderContext, string text, int x, int y) =>
        renderContext.DrawString(text, DashboardTheme.TitleFont, DashboardTheme.TextColor, x, y);

    public static void SectionHeader(RenderContext renderContext, string text, int x, ref int y)
    {
        renderContext.DrawString(text, DashboardTheme.HeaderFont, DashboardTheme.SecondaryTextColor, x, y);
        y += 18;
    }

    public static void Field(RenderContext renderContext, string label, string value, int x, ref int y, Color? valueColor = null, int valueOffset = 175)
    {
        renderContext.DrawString(label, DashboardTheme.BodyFont, DashboardTheme.SecondaryTextColor, x, y);
        renderContext.DrawString(value, DashboardTheme.BodyFont, valueColor ?? DashboardTheme.TextColor, x + valueOffset, y);
        y += 16;
    }

    public static void SmallField(RenderContext renderContext, string label, string value, int x, ref int y, Color? valueColor = null, int valueOffset = 180)
    {
        renderContext.DrawString(label, DashboardTheme.SmallFont, DashboardTheme.SecondaryTextColor, x, y);
        renderContext.DrawString(value, DashboardTheme.SmallFont, valueColor ?? DashboardTheme.TextColor, x + valueOffset, y);
        y += 13;
    }

    public static void StatusLine(RenderContext renderContext, string label, bool ok, int x, ref int y)
    {
        Color color = ok ? DashboardTheme.Green : DashboardTheme.Gray;
        string text = ok ? "✓ Executed" : "✗ Not Executed";
        SmallField(renderContext, label, text, x, ref y, color);
    }

    /// <summary>Badge de statut PASS/WARN/FAIL avec pastille colorée.</summary>
    public static void Badge(RenderContext renderContext, string label, HealthState state, int x, int y)
    {
        (Color color, string dot, string text) = state switch
        {
            HealthState.Pass => (DashboardTheme.Green, "\U0001F7E2", "PASS"),
            HealthState.Warn => (DashboardTheme.Orange, "\U0001F7E1", "WARN"),
            HealthState.Fail => (DashboardTheme.Red, "\U0001F534", "FAIL"),
            HealthState.Waiting => (DashboardTheme.Gray, "\U0001F7E1", "WAITING"),
            _ => (DashboardTheme.Gray, "⚪", "N/A")
        };

        renderContext.DrawString($"{dot} {label}", DashboardTheme.SmallFont, DashboardTheme.SecondaryTextColor, x, y);
        renderContext.DrawString(text, DashboardTheme.SmallFont, color, x + 90, y);
    }

    /// <summary>Barre horizontale de valeur 0..1 avec couleur d'état, style utilisé par le Market Profile.</summary>
    public static void ValueBar(RenderContext renderContext, string label, double value01, int x, int y, int width, int height)
    {
        double clamped = Math.Clamp(value01, 0.0, 1.0);
        Color color = DashboardTheme.StateColor(clamped);
        int filled = (int)Math.Round(width * clamped, MidpointRounding.AwayFromZero);

        renderContext.DrawString(label, DashboardTheme.BodyFont, DashboardTheme.TextColor, x, y);
        renderContext.DrawString(FormatPercent(clamped), DashboardTheme.BodyFont, color, x + width + 10, y);

        var bounds = new Rectangle(x, y + 16, width, height);
        renderContext.FillRectangle(DashboardTheme.BarBackground, bounds);
        renderContext.FillRectangle(color, new Rectangle(bounds.X, bounds.Y, filled, bounds.Height));
    }

    /// <summary>Jauge textuelle ASCII "██████████░░░░ 67%" demandée pour le Dataset Dashboard.</summary>
    public static void Gauge(RenderContext renderContext, string label, double value01, int x, int y, int blockCount = 20)
    {
        double clamped = Math.Clamp(value01, 0.0, 1.0);
        int filled = (int)Math.Round(blockCount * clamped, MidpointRounding.AwayFromZero);
        string bar = new string('█', filled) + new string('░', blockCount - filled);
        Color color = DashboardTheme.StateColor(clamped);

        renderContext.DrawString(label, DashboardTheme.BodyFont, DashboardTheme.SecondaryTextColor, x, y);
        renderContext.DrawString($"{bar} {FormatPercent(clamped)}", DashboardTheme.BodyFont, color, x, y + 15);
    }

    public static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "N/A";

        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    public static string FormatDate(DateTime? value) =>
        value is null ? "N/A" : value.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    public static string FormatDecimal(decimal? value) =>
        value is null ? "N/A" : value.Value.ToString(CultureInfo.InvariantCulture);

    public static string FormatDouble(double? value) =>
        value is null || !double.IsFinite(value.Value) ? "N/A" : value.Value.ToString("0.0000", CultureInfo.InvariantCulture);

    public static string FormatMs(double? milliseconds) =>
        milliseconds is null ? "N/A" : $"{milliseconds.Value.ToString("0.000", CultureInfo.InvariantCulture)} ms";

    public static string FormatInt(int? value) =>
        value is null ? "N/A" : value.Value.ToString(CultureInfo.InvariantCulture);

    public static string FormatOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "N/A" : value;

    public static string FormatPercent(double value) =>
        Math.Clamp(value, 0.0, 1.0).ToString("P0", CultureInfo.InvariantCulture);

    public static string FormatList(IReadOnlyList<string>? values, int take = 3)
    {
        if (values is null || values.Count == 0)
            return "N/A";

        return string.Join(" | ", values.Take(take));
    }

    public static string FormatMetrics(IReadOnlyDictionary<string, object>? values, int take = 3)
    {
        if (values is null || values.Count == 0)
            return "N/A";

        return string.Join(" | ", values.Take(take).Select(entry => $"{entry.Key}={entry.Value}"));
    }

    public static string SplitPascalCase(string value)
    {
        var chars = new List<char>(value.Length + 4);
        for (int index = 0; index < value.Length; index++)
        {
            if (index > 0 && char.IsUpper(value[index]) && !char.IsWhiteSpace(value[index - 1]))
                chars.Add(' ');

            chars.Add(value[index]);
        }

        return new string(chars.ToArray());
    }
}

/// <summary>État de santé synthétique utilisé par la System Health bar et les badges de dashboard.</summary>
internal enum HealthState
{
    Unknown,
    Waiting,
    Pass,
    Warn,
    Fail
}
