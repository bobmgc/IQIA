using System;
using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Engine.Entry;
using IQIAIndicator.Engine.ScientificFusion;

namespace IQIAIndicator.Engine.EntryTrigger;

public sealed class EntryTriggerBuilder
{
    private readonly List<string> _warnings = new();
    private readonly List<string> _diagnostics = new();
    private EntryTriggerStatus _triggerStatus = EntryTriggerStatus.INVALID;
    private DirectionCandidate _direction = DirectionCandidate.NO_ACTION;
    private double _scientificConfidence;
    private double _opportunityPriority;
    private EntryTriggerReason _reason = EntryTriggerReason.UNKNOWN;
    private double? _estimatedEquilibrium;
    private double? _distanceToEquilibrium;
    private decimal _currentPrice;

    public void Populate(EntryTriggerContext context)
    {
        _warnings.Clear();
        _diagnostics.Clear();
        _triggerStatus = EntryTriggerStatus.INVALID;
        _direction = DirectionCandidate.NO_ACTION;
        _scientificConfidence = 0.0;
        _opportunityPriority = 0.0;
        _reason = EntryTriggerReason.UNKNOWN;
        _estimatedEquilibrium = null;
        _distanceToEquilibrium = null;
        _currentPrice = 0m;

        if (context is null)
        {
            _diagnostics.Add("EntryTriggerContext is null.");
            _triggerStatus = EntryTriggerStatus.INVALID;
            _reason = EntryTriggerReason.INVALID_CONTEXT;
            return;
        }

        _scientificConfidence = context.ScientificAssessment.OverallConfidence;
        _opportunityPriority = context.EntryCandidate.OpportunityPriority;
        _currentPrice = context.BusinessContext.CurrentPrice;
        _estimatedEquilibrium = context.BusinessContext.EstimatedEquilibrium;
        _distanceToEquilibrium = context.BusinessContext.DistanceToEquilibrium;
        _direction = DetermineDirection(context);
        _triggerStatus = DetermineTriggerStatus(context);
        _reason = BuildReason(context);

        if (_currentPrice <= 0m)
        {
            _warnings.Add("Prix courant indisponible ou invalide.");
        }

        if (_estimatedEquilibrium is null)
        {
            _warnings.Add("Niveau d'équilibre estimé indisponible.");
        }

        if (context.EntryAssessment.BlockingIssues is { Count: > 0 })
        {
            _warnings.AddRange(context.EntryAssessment.BlockingIssues.Where(item => !string.IsNullOrWhiteSpace(item)));
        }

        if (context.EntryAssessment.Diagnostics is not null && context.EntryAssessment.Diagnostics.Count > 0)
        {
            _diagnostics.AddRange(context.EntryAssessment.Diagnostics.Where(diagnostic => !string.IsNullOrWhiteSpace(diagnostic)));
        }
    }

    public EntryTriggerCandidate Build(EntryTriggerContext context)
    {
        Populate(context);

        var assessment = new EntryTriggerAssessment(
            _triggerStatus,
            _direction,
            _scientificConfidence,
            _opportunityPriority,
            _reason,
            _estimatedEquilibrium,
            _distanceToEquilibrium,
            DateTime.UtcNow);

        return new EntryTriggerCandidate(
            assessment,
            context.EntryCandidate,
            _currentPrice,
            _warnings.AsReadOnly(),
            _diagnostics.AsReadOnly(),
            DateTime.UtcNow);
    }

    // Business data (current price, estimated equilibrium, dynamic z-score, distance, etc.)
    // are provided via EntryBusinessContext. EntryTrigger must not recompute scientific metrics.

    private static DirectionCandidate DetermineDirection(EntryTriggerContext context)
    {
        if (context.EntryCandidate.OpportunityStatus == OpportunityStatus.WATCHLIST)
        {
            return DirectionCandidate.WATCH;
        }

        if (TryGetDynamicZScore(context, out double zScore))
        {
            if (zScore > 0.0)
            {
                return DirectionCandidate.SELL_CANDIDATE;
            }

            if (zScore < 0.0)
            {
                return DirectionCandidate.BUY_CANDIDATE;
            }
        }

        return DirectionCandidate.NO_ACTION;
    }
    private static bool TryGetDynamicZScore(EntryTriggerContext context, out double zScore)
    {
        zScore = 0.0;

        if (context.BusinessContext.DynamicZScore is double dz && double.IsFinite(dz))
        {
            zScore = dz;
            return true;
        }

        // Fallback: inspect scientific results if business context did not contain the metric.
        if (context.ScientificAssessment?.ScientificResults is null)
        {
            return false;
        }

        foreach (var result in context.ScientificAssessment.ScientificResults)
        {
            if (!string.Equals(result.ModelName, "DynamicZScoreModel", StringComparison.OrdinalIgnoreCase) || result.Metrics is null)
            {
                continue;
            }

            if (result.Metrics.TryGetValue("DynamicZScore", out var raw) && raw is double typed && double.IsFinite(typed))
            {
                zScore = typed;
                return true;
            }
        }

        return false;
    }

    private EntryTriggerStatus DetermineTriggerStatus(EntryTriggerContext context)
    {
        if (context.EntryCandidate.OpportunityStatus == OpportunityStatus.NOT_QUALIFIED)
        {
            return context.EntryAssessment.BlockingIssues is { Count: > 0 }
                ? EntryTriggerStatus.EXPIRED
                : EntryTriggerStatus.NOT_READY;
        }

        if (context.EntryCandidate.OpportunityStatus == OpportunityStatus.WATCHLIST)
        {
            return EntryTriggerStatus.WATCH;
        }

        if (context.EntryCandidate.OpportunityStatus == OpportunityStatus.QUALIFIED)
        {
            return _scientificConfidence >= 0.70
                ? EntryTriggerStatus.READY
                : EntryTriggerStatus.WATCH;
        }

        if (context.EntryCandidate.OpportunityStatus == OpportunityStatus.HIGH_PRIORITY)
        {
            return _scientificConfidence >= 0.55
                ? EntryTriggerStatus.READY
                : EntryTriggerStatus.WATCH;
        }

        return EntryTriggerStatus.NOT_READY;
    }

    private EntryTriggerReason BuildReason(EntryTriggerContext context)
    {
        return _triggerStatus switch
        {
            EntryTriggerStatus.INVALID => EntryTriggerReason.INVALID_CONTEXT,
            EntryTriggerStatus.EXPIRED => EntryTriggerReason.BLOCKED,
            EntryTriggerStatus.NOT_READY => EntryTriggerReason.NOT_READY,
            EntryTriggerStatus.WATCH => EntryTriggerReason.WATCH,
            EntryTriggerStatus.READY => EntryTriggerReason.READY,
            _ => EntryTriggerReason.UNKNOWN
        };
    }
}
