using System;
using System.Drawing;
using IQIAIndicator.Engine.Presentation;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;

namespace IQIAIndicator.Infrastructure.ATAS;

public sealed class ATASDrawingFactory
{
    private static readonly RenderFont HeaderFont = new("Arial", 11f);
    private static readonly RenderFont BodyFont = new("Arial", 9f);

    private static readonly Color PanelBackground = Color.FromArgb(230, 28, 31, 35);
    private static readonly Color HighlightBackground = Color.FromArgb(238, 37, 48, 62);
    private static readonly Color PinnedBackground = Color.FromArgb(242, 42, 55, 48);
    private static readonly Color HiddenBackground = Color.Transparent;
    private static readonly Color Accent = Color.FromArgb(255, 75, 145, 210);
    private static readonly Color WarningAccent = Color.FromArgb(255, 225, 160, 42);
    private static readonly Color Text = Color.White;
    private static readonly Color SecondaryText = Color.LightGray;

    public ATASDrawing Create(ATASAnnotationDescriptor descriptor)
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        return descriptor.Visibility == ATASDrawingVisibility.Hidden
            ? ATASDrawing.Hidden
            : new ATASDrawing(
                descriptor.Bounds,
                SelectBackground(descriptor.Visibility),
                SelectAccent(descriptor.Style),
                Text,
                SecondaryText,
                HeaderFont,
                BodyFont,
                descriptor.Lines);
    }

    private static Color SelectBackground(ATASDrawingVisibility visibility)
        => visibility switch
        {
            ATASDrawingVisibility.Highlighted => HighlightBackground,
            ATASDrawingVisibility.Pinned => PinnedBackground,
            ATASDrawingVisibility.Visible => PanelBackground,
            _ => HiddenBackground
        };

    private static Color SelectAccent(ChartAnnotationStyle style)
        => style.Category == AnnotationCategory.Warning
            ? WarningAccent
            : Accent;
}

public sealed class ATASDrawing
{
    public static readonly ATASDrawing Hidden = new(
        Rectangle.Empty,
        Color.Transparent,
        Color.Transparent,
        Color.Transparent,
        Color.Transparent,
        new RenderFont("Arial", 1f),
        new RenderFont("Arial", 1f),
        Array.Empty<string>());

    private const int Padding = 8;
    private const int AccentWidth = 4;
    private const int HeaderLineHeight = 18;
    private const int BodyLineHeight = 16;
    private const int MaximumLineCount = 6;

    private readonly Rectangle _bounds;
    private readonly Color _background;
    private readonly Color _accent;
    private readonly Color _text;
    private readonly Color _secondaryText;
    private readonly RenderFont _headerFont;
    private readonly RenderFont _bodyFont;
    private readonly IReadOnlyList<string> _lines;

    public ATASDrawing(
        Rectangle bounds,
        Color background,
        Color accent,
        Color text,
        Color secondaryText,
        RenderFont headerFont,
        RenderFont bodyFont,
        IReadOnlyList<string> lines)
    {
        _bounds = bounds;
        _background = background;
        _accent = accent;
        _text = text;
        _secondaryText = secondaryText;
        _headerFont = headerFont;
        _bodyFont = bodyFont;
        _lines = lines;
    }

    public void RenderTo(RenderContext renderContext)
    {
        if (renderContext is null)
        {
            throw new ArgumentNullException(nameof(renderContext));
        }

        if (_bounds == Rectangle.Empty)
        {
            return;
        }

        renderContext.FillRectangle(_background, _bounds);
        renderContext.FillRectangle(_accent, new Rectangle(_bounds.X, _bounds.Y, AccentWidth, _bounds.Height));

        int x = _bounds.X + Padding + AccentWidth;
        int y = _bounds.Y + Padding;

        if (_lines.Count == 0)
        {
            return;
        }

        renderContext.DrawString(TrimLine(_lines[0]), _headerFont, _text, x, y);
        y += HeaderLineHeight;

        int bodyCount = Math.Min(_lines.Count - 1, MaximumLineCount - 1);
        for (int index = 0; index < bodyCount; index++)
        {
            renderContext.DrawString(TrimLine(_lines[index + 1]), _bodyFont, _secondaryText, x, y);
            y += BodyLineHeight;
        }
    }

    private static string TrimLine(string value)
        => value.Length <= 72 ? value : value[..69] + "...";
}
