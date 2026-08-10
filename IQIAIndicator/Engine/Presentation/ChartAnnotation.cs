using System;
using System.Collections.Generic;

namespace IQIAIndicator.Engine.Presentation;

public enum AnnotationType
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

public enum AnnotationAnchor
{
    CurrentBar,
    Price,
    Indicator,
    Viewport,
    Chart
}

public enum AnnotationVisibility
{
    Hidden,
    Visible,
    Highlighted,
    Pinned
}

public enum AnnotationImportance
{
    Hidden,
    Standard,
    Elevated,
    Prominent
}

public enum AnnotationCategory
{
    Display,
    Warning,
    Diagnostic
}

public enum AnnotationSeverity
{
    Neutral,
    Advisory
}

public enum AnnotationTheme
{
    Default,
    Emphasis
}

public sealed record ChartAnnotationStyle(
    AnnotationImportance Importance,
    AnnotationCategory Category,
    AnnotationSeverity Severity,
    AnnotationTheme Theme);

public sealed record ChartAnnotationPayload(
    string Title,
    string Subtitle,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyDictionary<string, object> Metrics);

public sealed record ChartAnnotation(
    AnnotationType AnnotationType,
    AnnotationAnchor Anchor,
    int Priority,
    AnnotationVisibility Visibility,
    ChartAnnotationPayload Payload,
    ChartAnnotationStyle Style,
    DateTime CreatedAt);
