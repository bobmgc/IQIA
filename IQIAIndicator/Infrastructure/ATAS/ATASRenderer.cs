using System;
using System.Collections.Generic;
using System.Drawing;
using ATAS.Indicators;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Presentation;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;

namespace IQIAIndicator.Infrastructure.ATAS;

public sealed class ATASRenderer
{
    // Sprint 15.24 (Lot 1 - chart display): mirrors ATASDrawingFactory's own self-contained palette -
    // Infrastructure/ATAS never reaches into Visualization/Rendering (DashboardTheme) for colors, to
    // avoid a new cross-layer dependency for a one-off palette.
    private static readonly Color EntryColor = Color.FromArgb(255, 75, 145, 210);
    private static readonly Color TakeProfitColor = Color.FromArgb(255, 46, 160, 90);
    private static readonly Color StopLossColor = Color.FromArgb(255, 210, 70, 70);
    private static readonly RenderFont LevelFont = new("Arial", 9f);
    private const int LevelLineWidth = 1;
    private const int MarkerHalfSize = 6;
    private const int LabelPadding = 6;

    private readonly ATASAnnotationMapper _annotationMapper;
    private readonly ATASCoordinateMapper _coordinateMapper;
    private readonly ATASDrawingFactory _drawingFactory;

    public ATASRenderer()
        : this(new ATASAnnotationMapper(), new ATASCoordinateMapper(), new ATASDrawingFactory())
    {
    }

    public ATASRenderer(
        ATASAnnotationMapper annotationMapper,
        ATASCoordinateMapper coordinateMapper,
        ATASDrawingFactory drawingFactory)
    {
        _annotationMapper = annotationMapper ?? throw new ArgumentNullException(nameof(annotationMapper));
        _coordinateMapper = coordinateMapper ?? throw new ArgumentNullException(nameof(coordinateMapper));
        _drawingFactory = drawingFactory ?? throw new ArgumentNullException(nameof(drawingFactory));
    }

    public void Render(RenderContext renderContext, ChartAnnotationCandidate candidate)
    {
        if (renderContext is null)
        {
            throw new ArgumentNullException(nameof(renderContext));
        }

        if (candidate is null)
        {
            throw new ArgumentNullException(nameof(candidate));
        }

        if (candidate.Annotations is null || candidate.Annotations.Count == 0)
        {
            return;
        }

        for (int index = 0; index < candidate.Annotations.Count; index++)
        {
            ChartAnnotation annotation = candidate.Annotations[index];
            var bounds = _coordinateMapper.Map(annotation.Anchor, index);
            ATASAnnotationDescriptor descriptor = _annotationMapper.Map(annotation, bounds);
            ATASDrawing drawing = _drawingFactory.Create(descriptor);

            drawing.RenderTo(renderContext);
        }
    }

    public IReadOnlyDictionary<string, string> Describe(ChartAnnotationCandidate candidate)
    {
        if (candidate is null)
            throw new ArgumentNullException(nameof(candidate));

        if (candidate.Annotations is null || candidate.Annotations.Count == 0)
            return new Dictionary<string, string>
            {
                ["Position"] = "None",
                ["Color"] = "None",
                ["Text"] = string.Empty
            };

        ChartAnnotation annotation = candidate.Annotations[0];
        var bounds = _coordinateMapper.Map(annotation.Anchor, 0);
        ATASAnnotationDescriptor descriptor = _annotationMapper.Map(annotation, bounds);
        return new Dictionary<string, string>
        {
            ["Position"] = bounds.ToString(),
            ["Color"] = $"{descriptor.Style.Category}/{descriptor.Style.Theme}/{descriptor.Style.Severity}",
            ["Text"] = string.Join(" | ", descriptor.Lines)
        };
    }

    /// <summary>
    /// Sprint 15.24 (Lot 1 - chart display). Draws TradePlanAnnotationCandidate's price levels as
    /// horizontal lines spanning the chart area, using IChartContainer.GetYByPrice for the real
    /// price-to-pixel conversion (ATAS SDK, ATAS.Indicators.dll 7.0.9.461 - confirmed via reflection
    /// against the project's own referenced assembly before writing this method). Computes no price:
    /// every value drawn here is read unchanged from a TradePlanLevel already built by
    /// TradePlanAnnotationBuilder from TradePlan.
    /// </summary>
    public void RenderTradePlan(
        RenderContext renderContext,
        IChartContainer priceContainer,
        Rectangle chartArea,
        TradePlanAnnotationCandidate candidate)
    {
        if (renderContext is null)
        {
            throw new ArgumentNullException(nameof(renderContext));
        }

        if (priceContainer is null)
        {
            throw new ArgumentNullException(nameof(priceContainer));
        }

        if (candidate is null)
        {
            throw new ArgumentNullException(nameof(candidate));
        }

        if (candidate.Levels is null || candidate.Levels.Count == 0)
        {
            return;
        }

        int x1 = chartArea.X;
        int x2 = chartArea.X + chartArea.Width;

        foreach (TradePlanLevel level in candidate.Levels)
        {
            Color color = SelectColor(level.Kind);
            int y = priceContainer.GetYByPrice(level.Price, false);

            renderContext.DrawLine(new RenderPen(color, LevelLineWidth), x1, y, x2, y);
            renderContext.DrawString($"{DescribeKind(level.Kind)} {level.Price}", LevelFont, color, x1 + LabelPadding, y - (int)LevelFont.Size - 2);

            if (level.Kind == TradePlanLevelKind.Entry)
            {
                DrawDirectionMarker(renderContext, candidate.Direction, x1 + LabelPadding, y, color);
            }
        }
    }

    private static Color SelectColor(TradePlanLevelKind kind)
        => kind switch
        {
            TradePlanLevelKind.Entry => EntryColor,
            TradePlanLevelKind.TakeProfit => TakeProfitColor,
            TradePlanLevelKind.StopLoss => StopLossColor,
            _ => EntryColor
        };

    private static string DescribeKind(TradePlanLevelKind kind)
        => kind switch
        {
            TradePlanLevelKind.Entry => "ENTRY",
            TradePlanLevelKind.TakeProfit => "TP",
            TradePlanLevelKind.StopLoss => "SL",
            _ => kind.ToString()
        };

    /// <summary>
    /// Small triangle at the entry level, pointing in the direction of the trade (up for a long, down
    /// for a short). WATCH/NO_ACTION draw no marker - TradePlanBuilder never produces an EntryPrice for
    /// those directions (see TradePlanBuilder.cs), so this branch is defensive, not reachable today.
    /// </summary>
    private static void DrawDirectionMarker(RenderContext renderContext, DirectionCandidate direction, int x, int y, Color color)
    {
        Point[] triangle = direction switch
        {
            DirectionCandidate.BUY_CANDIDATE => new[]
            {
                new Point(x, y - MarkerHalfSize),
                new Point(x - MarkerHalfSize, y + MarkerHalfSize),
                new Point(x + MarkerHalfSize, y + MarkerHalfSize)
            },
            DirectionCandidate.SELL_CANDIDATE => new[]
            {
                new Point(x, y + MarkerHalfSize),
                new Point(x - MarkerHalfSize, y - MarkerHalfSize),
                new Point(x + MarkerHalfSize, y - MarkerHalfSize)
            },
            _ => Array.Empty<Point>()
        };

        if (triangle.Length > 0)
        {
            renderContext.FillPolygon(color, triangle);
        }
    }
}
