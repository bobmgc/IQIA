using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace IQIAIndicator.Engine.Presentation;

public sealed class OpportunityPresentationBuilder
{
    private const string DefaultTitle = "Opportunité IQIA";
    private const string DefaultSubtitle = "Aucune annotation disponible";
    private const string DefaultStatus = "Indisponible";
    private const string DefaultSummary = "Aucun résumé descriptif disponible.";
    private const string DefaultSignal = "Aucun signal";
    private const string DefaultRisk = "Risque indéfini";

    private readonly List<string> _supportingEvidence = new();
    private readonly List<string> _blockingIssues = new();
    private readonly List<string> _warnings = new();
    private readonly List<string> _diagnostics = new();
    private readonly Dictionary<string, object> _metrics = new();
    private string _title = DefaultTitle;
    private string _subtitle = DefaultSubtitle;
    private string _opportunityStatus = DefaultStatus;
    private string _signalLabel = DefaultSignal;
    private string _riskLabel = DefaultRisk;
    private int _opportunityPriority;

    public void Populate(ChartAnnotationCandidate chartAnnotationCandidate)
    {
        Clear();

        if (chartAnnotationCandidate is null)
        {
            _warnings.Add("ChartAnnotationCandidate missing.");
            _diagnostics.Add("Opportunity presentation could not be populated.");
            return;
        }

        AddDistinct(_warnings, chartAnnotationCandidate.Warnings);
        AddDistinct(_diagnostics, chartAnnotationCandidate.Diagnostics);

        if (chartAnnotationCandidate.Annotations is null || chartAnnotationCandidate.Annotations.Count == 0)
        {
            return;
        }

        ChartAnnotation primaryAnnotation = SelectPrimaryAnnotation(chartAnnotationCandidate.Annotations);
        ChartAnnotationPayload payload = primaryAnnotation.Payload;

        _title = FirstText(payload?.Title, DefaultTitle);
        _opportunityStatus = FirstText(payload?.Title, primaryAnnotation.Visibility.ToString());
        _opportunityPriority = primaryAnnotation.Priority;
        _subtitle = FirstText(payload?.Subtitle, $"{_opportunityStatus} | Priorité {_opportunityPriority}");
        _signalLabel = DetermineSignalLabel(primaryAnnotation);
        _riskLabel = DetermineRiskLabel(_opportunityPriority, _warnings.Count, _diagnostics.Count);

        foreach (ChartAnnotation annotation in chartAnnotationCandidate.Annotations)
        {
            ChartAnnotationPayload annotationPayload = annotation.Payload;
            if (annotationPayload is null)
            {
                continue;
            }

            AddDistinct(_supportingEvidence, annotationPayload.Reasons);
            AddDistinct(_warnings, annotationPayload.Warnings);
            AddDistinct(_diagnostics, annotationPayload.Diagnostics);
            AddMetrics(annotationPayload.Metrics);
        }
    }

    public OpportunityPresentation Build()
        => new(
            _title,
            _subtitle,
            _opportunityStatus,
            _opportunityPriority,
            _signalLabel,
            _riskLabel,
            BuildScientificSummary(),
            ImmutableArray.CreateRange(_supportingEvidence),
            ImmutableArray.CreateRange(_blockingIssues),
            ImmutableArray.CreateRange(_warnings),
            ImmutableArray.CreateRange(_diagnostics),
            _metrics.ToImmutableDictionary(),
            DateTime.UtcNow);

    private void Clear()
    {
        _supportingEvidence.Clear();
        _blockingIssues.Clear();
        _warnings.Clear();
        _diagnostics.Clear();
        _metrics.Clear();
        _title = DefaultTitle;
        _subtitle = DefaultSubtitle;
        _opportunityStatus = DefaultStatus;
        _opportunityPriority = 0;
    }

    private string BuildScientificSummary()
    {
        var parts = new List<string>();

        parts.AddRange(_supportingEvidence);

        return parts.Count == 0
            ? FirstText(_signalLabel, DefaultSummary)
            : string.Join(" | ", parts);
    }

    private static string DetermineSignalLabel(ChartAnnotation annotation)
        => annotation.AnnotationType switch
        {
            AnnotationType.Arrow => "Signal directionnel manuel",
            AnnotationType.Badge => "Signal manuel prioritaire",
            AnnotationType.Label => "Observation manuelle",
            AnnotationType.InformationBox => "Observation du marché",
            _ => DefaultSignal
        };

    private static string DetermineRiskLabel(int priority, int warningCount, int diagnosticCount)
    {
        if (warningCount > 0 || diagnosticCount > 0)
        {
            return "Risque accru";
        }

        return priority switch
        {
            >= 3 => "Risque faible",
            2 => "Risque moyen",
            1 => "Risque élevé",
            _ => DefaultRisk
        };
    }

    private static ChartAnnotation SelectPrimaryAnnotation(IEnumerable<ChartAnnotation> annotations)
    {
        ChartAnnotation? primary = null;

        foreach (ChartAnnotation annotation in annotations)
        {
            if (primary is null || annotation.Priority > primary.Priority)
            {
                primary = annotation;
            }
        }

        return primary!;
    }

    private void AddMetrics(IEnumerable<KeyValuePair<string, object>>? metrics)
    {
        if (metrics is null)
        {
            return;
        }

        foreach (var metric in metrics)
        {
            if (!_metrics.ContainsKey(metric.Key))
            {
                _metrics.Add(metric.Key, metric.Value);
            }
        }
    }

    private static void AddDistinct(List<string> target, IEnumerable<string> source)
    {
        if (source is null)
        {
            return;
        }

        foreach (string item in source.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            if (!target.Contains(item))
            {
                target.Add(item);
            }
        }
    }

    private static void AddIfAvailable(List<string> target, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target.Add(value);
        }
    }

    private static string FirstText(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
