using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using IQIAIndicator.Engine.EntryTrigger;
using IQIAIndicator.Engine.Visualization;

namespace IQIAIndicator.Engine.Presentation;

public sealed class ChartAnnotationBuilder
{
    private readonly List<ChartAnnotation> _annotations = new();
    private readonly List<string> _diagnostics = new();
    private readonly List<string> _warnings = new();

    public void Populate(VisualizationCandidate visualizationCandidate)
    {
        _annotations.Clear();
        _diagnostics.Clear();
        _warnings.Clear();

        if (visualizationCandidate is null)
        {
            _warnings.Add("VisualizationCandidate missing.");
            _diagnostics.Add("Chart annotation candidate could not be populated.");
            return;
        }

        AddDistinct(_warnings, visualizationCandidate.Warnings);
        AddDistinct(_diagnostics, visualizationCandidate.Diagnostics);

        var assessment = visualizationCandidate.Assessment;
        if (assessment is null)
        {
            _warnings.Add("VisualizationAssessment missing.");
            _diagnostics.Add("Chart annotation could not be created.");
            return;
        }

        AddDistinct(_warnings, assessment.Warnings);
        AddDistinct(_diagnostics, assessment.Diagnostics);

        _annotations.Add(CreateAnnotation(assessment));

        if (TryCreateDirectionAnnotation(assessment, out ChartAnnotation? directionAnnotation))
        {
            _annotations.Add(directionAnnotation!);
        }
    }

    public ChartAnnotationCandidate Build()
        => new(
            _annotations.ToArray(),
            DateTime.UtcNow,
            _diagnostics.ToArray(),
            _warnings.ToArray());

    private ChartAnnotation CreateAnnotation(VisualizationAssessment assessment)
    {
        var warnings = _warnings.ToArray();
        var diagnostics = _diagnostics.ToArray();

        string title = assessment.DisplayStatus switch
        {
            DisplayStatus.FEATURED => "Opportunité de trade manuel",
            DisplayStatus.HIGHLIGHTED => "Observation prioritaire du marché",
            DisplayStatus.VISIBLE => "Observation du marché",
            _ => "Aucune opportunité détectée"
        };

        string subtitle = assessment.DisplayReasons is { Count: > 0 }
            ? string.Join(" | ", assessment.DisplayReasons.Where(reason => !string.IsNullOrWhiteSpace(reason)))
            : $"Priorité {assessment.DisplayPriority}";

        var payload = new ChartAnnotationPayload(
            title,
            subtitle,
            assessment.DisplayReasons?.Where(reason => !string.IsNullOrWhiteSpace(reason)).ToArray() ?? Array.Empty<string>(),
            warnings,
            diagnostics,
            ImmutableDictionary<string, object>.Empty
                .Add("DisplayStatus", assessment.DisplayStatus.ToString())
                .Add("DisplayPriority", assessment.DisplayPriority));

        var annotationType = assessment.DisplayStatus switch
        {
            DisplayStatus.FEATURED => AnnotationType.Badge,
            DisplayStatus.HIGHLIGHTED => AnnotationType.Label,
            DisplayStatus.VISIBLE => AnnotationType.InformationBox,
            _ => AnnotationType.InformationBox
        };

        return new ChartAnnotation(
            annotationType,
            AnnotationAnchor.CurrentBar,
            assessment.DisplayPriority,
            ToVisibility(assessment.DisplayStatus),
            payload,
            CreateStyle(assessment.DisplayStatus, warnings.Length > 0),
            DateTime.UtcNow);
    }

    /// <summary>
    /// Sprint 15.6 (BC-07). Adds a directional annotation using the EXISTING <see cref="AnnotationType.Arrow"/>
    /// value - no new AnnotationType is introduced. Direction comes from
    /// <see cref="VisualizationAssessment.Direction"/> alone, which is itself carried through unchanged
    /// from <see cref="EntryTriggerAssessment.Direction"/> (Sprint 15.5's single source of truth) -
    /// this method never reads DynamicZScore or any other raw metric, and never fabricates a direction.
    /// BUY_CANDIDATE/SELL_CANDIDATE produce an Arrow (up/down encoded in the payload, since Arrow is a
    /// single AnnotationType - see the Direction/ArrowDirection metrics and Title/Subtitle below);
    /// WATCH, NO_ACTION, and a missing Direction all produce no Arrow at all - there is no default
    /// direction. Anchored at <see cref="AnnotationAnchor.CurrentBar"/>, the same anchor every other
    /// annotation already uses - this sprint does not add real bar/price chart-space positioning
    /// (ATASCoordinateMapper/ATASAnnotationMapper/ATASDrawingFactory are untouched; see this sprint's
    /// report for why no such mechanism exists yet to reuse).
    /// </summary>
    private static bool TryCreateDirectionAnnotation(VisualizationAssessment assessment, out ChartAnnotation? annotation)
    {
        annotation = null;

        (string label, string arrowGlyph) = assessment.Direction switch
        {
            DirectionCandidate.BUY_CANDIDATE => ("BUY", "Arrow Up"),
            DirectionCandidate.SELL_CANDIDATE => ("SELL", "Arrow Down"),
            _ => (string.Empty, string.Empty)
        };

        if (label.Length == 0)
        {
            return false;
        }

        var payload = new ChartAnnotationPayload(
            $"{label} Signal",
            arrowGlyph,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            ImmutableDictionary<string, object>.Empty
                .Add("Direction", label)
                .Add("ArrowDirection", arrowGlyph));

        // Priority fixed above the status annotation's range (0-3, see ToDisplayPriority): a confirmed
        // directional signal is the single most actionable item on this bar, so it must win
        // OpportunityPresentationBuilder.SelectPrimaryAnnotation's priority comparison whenever present.
        const int directionAnnotationPriority = 10;

        annotation = new ChartAnnotation(
            AnnotationType.Arrow,
            AnnotationAnchor.CurrentBar,
            directionAnnotationPriority,
            AnnotationVisibility.Pinned,
            payload,
            new ChartAnnotationStyle(AnnotationImportance.Prominent, AnnotationCategory.Display, AnnotationSeverity.Neutral, AnnotationTheme.Emphasis),
            DateTime.UtcNow);

        return true;
    }

    private static ChartAnnotationStyle CreateStyle(DisplayStatus displayStatus, bool hasWarnings)
        => new(
            ToImportance(displayStatus),
            hasWarnings ? AnnotationCategory.Warning : AnnotationCategory.Display,
            hasWarnings ? AnnotationSeverity.Advisory : AnnotationSeverity.Neutral,
            displayStatus == DisplayStatus.FEATURED ? AnnotationTheme.Emphasis : AnnotationTheme.Default);

    private static AnnotationVisibility ToVisibility(DisplayStatus displayStatus)
        => displayStatus switch
        {
            DisplayStatus.VISIBLE => AnnotationVisibility.Visible,
            DisplayStatus.HIGHLIGHTED => AnnotationVisibility.Highlighted,
            DisplayStatus.FEATURED => AnnotationVisibility.Pinned,
            _ => AnnotationVisibility.Hidden
        };

    private static AnnotationImportance ToImportance(DisplayStatus displayStatus)
        => displayStatus switch
        {
            DisplayStatus.VISIBLE => AnnotationImportance.Standard,
            DisplayStatus.HIGHLIGHTED => AnnotationImportance.Elevated,
            DisplayStatus.FEATURED => AnnotationImportance.Prominent,
            _ => AnnotationImportance.Hidden
        };

    private static void AddDistinct(List<string> target, IReadOnlyList<string> source)
    {
        if (source is null || source.Count == 0)
        {
            return;
        }

        foreach (var item in source.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            if (!target.Contains(item))
            {
                target.Add(item);
            }
        }
    }
}
