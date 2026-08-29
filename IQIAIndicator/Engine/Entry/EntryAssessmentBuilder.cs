using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Engine.ScientificFusion;

namespace IQIAIndicator.Engine.Entry;

public sealed class EntryAssessmentBuilder
{
    private readonly List<string> _diagnostics = new();
    private readonly List<string> _warnings = new();
    private readonly List<string> _blocking = new();
    private readonly List<string> _supporting = new();

    public void Populate(ScientificAssessment scientificAssessment)
    {
        _diagnostics.Clear();
        _warnings.Clear();
        _blocking.Clear();
        _supporting.Clear();

        if (scientificAssessment is null)
        {
            _diagnostics.Add("ScientificAssessment is null.");
            _blocking.Add("ScientificAssessment missing");
            return;
        }

        // Copy diagnostics from scientific assessment if present
        if (!string.IsNullOrWhiteSpace(scientificAssessment.Diagnostics))
        {
            _diagnostics.Add(scientificAssessment.Diagnostics);
        }

        // Missing evidence becomes blocking issues
        if (scientificAssessment.MissingEvidence is not null && scientificAssessment.MissingEvidence.Count > 0)
        {
            _blocking.AddRange(scientificAssessment.MissingEvidence);
            _warnings.Add("Missing evidence detected.");
        }

        // Evidence conflicts are blocking
        if (scientificAssessment.EvidenceConflict is not null && scientificAssessment.EvidenceConflict.Count > 0)
        {
            _blocking.AddRange(scientificAssessment.EvidenceConflict);
            _warnings.Add("Evidence conflict detected.");
        }

        // Supporting evidence: list successful models and agreements
        if (scientificAssessment.SuccessfulModels is not null && scientificAssessment.SuccessfulModels.Count > 0)
        {
            _supporting.AddRange(scientificAssessment.SuccessfulModels);
        }

        if (scientificAssessment.EvidenceAgreement is not null && scientificAssessment.EvidenceAgreement.Count > 0)
        {
            _supporting.AddRange(scientificAssessment.EvidenceAgreement.Where(x => !_supporting.Contains(x)));
        }

        // Diagnostics completeness warning
        if (scientificAssessment.Diagnostics is null || string.IsNullOrWhiteSpace(scientificAssessment.Diagnostics))
        {
            _warnings.Add("Scientific assessment diagnostics are empty.");
        }
    }

    public EntryAssessment Build(ScientificAssessment scientificAssessment)
    {
        double quality = scientificAssessment?.OverallConfidence ?? 0.0;

        // Determine readiness
        EntryReadiness readiness;
        if (_blocking.Count > 0)
        {
            readiness = EntryReadiness.INSUFFICIENT_EVIDENCE;
        }
        else if (_supporting.Count == 0)
        {
            readiness = EntryReadiness.INSUFFICIENT_EVIDENCE;
        }
        else
        {
            // If all expected models were successful and no conflicts -> ready
            var expected = scientificAssessment is null ? 0 : scientificAssessment.ExecutedModels?.Count ?? 0;
            var successful = scientificAssessment is null ? 0 : scientificAssessment.SuccessfulModels?.Count ?? 0;
            if (expected > 0 && successful == expected)
            {
                readiness = EntryReadiness.READY_FOR_NEXT_STAGE;
            }
            else
            {
                readiness = EntryReadiness.PARTIAL;
            }
        }

        // Build opportunity reasons from supporting evidence and scientific result metrics
        var reasons = new List<string>();
        reasons.AddRange(_supporting);

        if (scientificAssessment?.ScientificResults is not null)
        {
            foreach (var result in scientificAssessment.ScientificResults)
            {
                if (result.Metrics is null) continue;
                if (result.Metrics.TryGetValue("SPRTDecision", out var sprt) && sprt is string s)
                {
                    reasons.Add($"SPRT:{s}");
                }

                if (result.Metrics.TryGetValue("VolatilityRegime", out var vr) && vr is string vrstr)
                {
                    reasons.Add($"VolatilityRegime:{vrstr}");
                }

                if (result.Metrics.TryGetValue("DynamicZScore", out var dz) && dz is double dzd)
                {
                    reasons.Add($"DynamicZScore:{dzd:F2}");
                }
            }
        }

        // Opportunity priority: use assessment quality as a descriptive ordering metric
        double priority = quality;

        // Determine opportunity status from blocking/supporting/quality
        OpportunityStatus status;
        if (_blocking.Count > 0)
        {
            status = OpportunityStatus.NOT_QUALIFIED;
        }
        else if (priority >= 0.9)
        {
            status = OpportunityStatus.HIGH_PRIORITY;
        }
        else if (priority >= 0.6)
        {
            status = OpportunityStatus.QUALIFIED;
        }
        else if (priority >= 0.3)
        {
            status = OpportunityStatus.WATCHLIST;
        }
        else
        {
            status = OpportunityStatus.NOT_QUALIFIED;
        }

        return new EntryAssessment(
            scientificAssessment!,
            quality,
            readiness,
            status,
            priority,
            reasons.AsReadOnly(),
            _blocking.AsReadOnly(),
            _supporting.AsReadOnly(),
            _warnings.AsReadOnly(),
            _diagnostics.AsReadOnly());
    }
}
