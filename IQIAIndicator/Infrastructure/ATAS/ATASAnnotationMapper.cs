using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using IQIAIndicator.Engine.Presentation;

namespace IQIAIndicator.Infrastructure.ATAS;

public enum ATASDrawingKind
{
    Arrow,
    Badge,
    Label,
    Panel,
    Zone,
    Marker,
    Tooltip,
    InformationBox
}

public enum ATASDrawingVisibility
{
    Hidden,
    Visible,
    Highlighted,
    Pinned
}

public sealed record ATASAnnotationDescriptor(
    ATASDrawingKind Kind,
    Rectangle Bounds,
    int Priority,
    ATASDrawingVisibility Visibility,
    IReadOnlyList<string> Lines,
    ChartAnnotationStyle Style);

public sealed class ATASAnnotationMapper
{
    public ATASAnnotationDescriptor Map(ChartAnnotation annotation, Rectangle bounds)
    {
        if (annotation is null)
        {
            throw new ArgumentNullException(nameof(annotation));
        }

        return new ATASAnnotationDescriptor(
            ToDrawingKind(annotation.AnnotationType),
            bounds,
            annotation.Priority,
            ToVisibility(annotation.Visibility),
            BuildLines(annotation.Payload),
            annotation.Style);
    }

    private static ATASDrawingKind ToDrawingKind(AnnotationType annotationType)
        => annotationType switch
        {
            AnnotationType.Arrow => ATASDrawingKind.Arrow,
            AnnotationType.Badge => ATASDrawingKind.Badge,
            AnnotationType.Label => ATASDrawingKind.Label,
            AnnotationType.Panel => ATASDrawingKind.Panel,
            AnnotationType.Zone => ATASDrawingKind.Zone,
            AnnotationType.Marker => ATASDrawingKind.Marker,
            AnnotationType.Tooltip => ATASDrawingKind.Tooltip,
            _ => ATASDrawingKind.InformationBox
        };

    private static ATASDrawingVisibility ToVisibility(AnnotationVisibility visibility)
        => visibility switch
        {
            AnnotationVisibility.Visible => ATASDrawingVisibility.Visible,
            AnnotationVisibility.Highlighted => ATASDrawingVisibility.Highlighted,
            AnnotationVisibility.Pinned => ATASDrawingVisibility.Pinned,
            _ => ATASDrawingVisibility.Hidden
        };

    private static IReadOnlyList<string> BuildLines(ChartAnnotationPayload payload)
    {
        var lines = new List<string>();

        AddLine(lines, payload.Title);
        AddLine(lines, payload.Subtitle);
        AddPrefixed(lines, "Reason", payload.Reasons);
        AddPrefixed(lines, "Warning", payload.Warnings);
        AddPrefixed(lines, "Diagnostic", payload.Diagnostics);

        if (payload.Metrics is not null && payload.Metrics.Count > 0)
        {
            foreach (var metric in payload.Metrics)
            {
                AddLine(lines, $"{metric.Key}: {metric.Value}");
            }
        }

        return lines.ToArray();
    }

    private static void AddPrefixed(List<string> lines, string prefix, IReadOnlyList<string> values)
    {
        if (values is null || values.Count == 0)
        {
            return;
        }

        foreach (string value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            AddLine(lines, $"{prefix}: {value}");
        }
    }

    private static void AddLine(List<string> lines, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add(value);
        }
    }
}
