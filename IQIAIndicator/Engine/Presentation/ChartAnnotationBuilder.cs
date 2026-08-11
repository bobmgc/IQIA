using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
