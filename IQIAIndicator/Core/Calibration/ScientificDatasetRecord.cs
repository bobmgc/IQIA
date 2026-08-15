using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IQIAIndicator.Core.Observability;
using IQIAIndicator.Engine.Decision.Arbitration;
using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.ScientificFusion;
using IQIAIndicator.Engine.ScientificModels.Abstractions;

namespace IQIAIndicator.Core.Calibration;

using ScientificMarketContext = global::IQIAIndicator.Engine.ScientificModels.Abstractions.MarketContext;

// Sprint 15.17 (QDE-012 real-market capture): Open/High/Low/Volume added as trailing OPTIONAL
// positional parameters (default 0m) specifically so the two pre-existing call sites that construct
// this record positionally (Tests/Calibration/ScientificDatasetCollectorTests.cs,
// Tests/Dashboards/DatasetDashboardStatusTests.cs) keep compiling unchanged. CurrentPrice is kept as
// the historical field name (it already carries the bar's Close - see From() below) rather than
// renamed, for the same reason. Do not reorder existing parameters.
public sealed record ScientificDatasetRecord(
    Guid SessionId,
    DateTime Timestamp,
    string Symbol,
    string TimeFrame,
    decimal CurrentPrice,
    int HistoryLength,
    int CurrentBar,
    IReadOnlyDictionary<string, double?> Metrics,
    IReadOnlyDictionary<string, string> Categories,
    decimal Open = 0m,
    decimal High = 0m,
    decimal Low = 0m,
    decimal Volume = 0m)
{
    /// <summary>Close is CurrentPrice under its historical name - see the ScientificDatasetRecord doc
    /// comment above. Exposed under its OHLCV name too so consumers (e.g. the OHLCV CSV export) never
    /// have to know about the historical alias.</summary>
    public decimal Close => CurrentPrice;

    public static ScientificDatasetRecord From(
        Guid sessionId,
        int currentBar,
        ScientificMarketContext marketContext,
        ScientificAssessment scientificAssessment,
        DecisionResult decisionResult,
        decimal open,
        decimal high,
        decimal low,
        decimal volume,
        PipelineTraceRun? trace = null)
    {
        if (sessionId == Guid.Empty)
            throw new ArgumentException("SessionId is required.", nameof(sessionId));

        ArgumentNullException.ThrowIfNull(marketContext);
        ArgumentNullException.ThrowIfNull(scientificAssessment);
        ArgumentNullException.ThrowIfNull(decisionResult);

        var metrics = new Dictionary<string, double?>(StringComparer.Ordinal);
        var categories = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (ScientificModelResult result in scientificAssessment.ScientificResults)
        {
            AddMetric(metrics, categories, result.ModelName, "Score", result.Score);
            if (result.Metrics is null)
                continue;

            foreach (KeyValuePair<string, object> metric in result.Metrics)
            {
                if (TryConvertDouble(metric.Value, out double numericValue))
                    AddMetric(metrics, categories, result.ModelName, metric.Key, numericValue);
                else if (metric.Value is string text)
                    categories[$"{result.ModelName}.{metric.Key}"] = text;
            }
        }

        AddMetric(metrics, categories, "Fusion", "OverallConfidence", scientificAssessment.OverallConfidence);
        AddMetric(metrics, categories, "Decision", "ScientificScore", FirstCandidate(decisionResult)?.ScientificScore);
        AddMetric(metrics, categories, "Decision", "QualityScore", FirstCandidate(decisionResult)?.QualityScore);
        AddMetric(metrics, categories, "Decision", "FinalScore", decisionResult.WinnerScore);

        categories["Fusion.SuccessfulModels"] = string.Join(";", scientificAssessment.SuccessfulModels);
        categories["Fusion.FailedModels"] = string.Join(";", scientificAssessment.FailedModels);
        categories["Fusion.EvidenceAgreement"] = string.Join(";", scientificAssessment.EvidenceAgreement);
        categories["Fusion.EvidenceConflict"] = string.Join(";", scientificAssessment.EvidenceConflict);
        categories["Fusion.MissingEvidence"] = string.Join(";", scientificAssessment.MissingEvidence);
        categories["Decision.TriggeredRules"] = string.Join(";", decisionResult.TriggeredRules);
        categories["Decision.RejectedRules"] = string.Join(";", decisionResult.RejectedRules);
        categories["Decision.Winner"] = decisionResult.Winner.ToString();
        categories["Decision.RuleExplanation"] = decisionResult.RuleExplanation;
        categories["Decision.ArbitrationExplanation"] = decisionResult.ArbitrationExplanation;
        categories["Fusion.Diagnostics"] = scientificAssessment.Diagnostics;

        if (trace is not null)
        {
            foreach (PipelineTraceEvent traceEvent in trace.Events)
                AddMetric(metrics, categories, $"Trace.{traceEvent.Stage}", "ElapsedMs", traceEvent.Elapsed.TotalMilliseconds);
        }

        return new ScientificDatasetRecord(
            sessionId,
            marketContext.Timestamp,
            marketContext.Symbol ?? string.Empty,
            marketContext.TimeFrame ?? string.Empty,
            marketContext.CurrentPrice,
            marketContext.History.Count,
            currentBar,
            metrics,
            categories,
            open,
            high,
            low,
            volume);
    }

    private static DecisionCandidate? FirstCandidate(DecisionResult result) =>
        result.Candidates.Length == 0 ? null : result.Candidates[0];

    private static void AddMetric(
        IDictionary<string, double?> metrics,
        IDictionary<string, string> categories,
        string owner,
        string name,
        double? value)
    {
        string key = $"{owner}.{name}";
        metrics[key] = value;
        categories[key] = value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static bool TryConvertDouble(object value, out double result)
    {
        switch (value)
        {
            case double doubleValue when double.IsFinite(doubleValue):
                result = doubleValue;
                return true;
            case float floatValue when float.IsFinite(floatValue):
                result = floatValue;
                return true;
            case decimal decimalValue:
                result = (double)decimalValue;
                return double.IsFinite(result);
            case int intValue:
                result = intValue;
                return true;
            case long longValue:
                result = longValue;
                return true;
            default:
                result = 0.0;
                return false;
        }
    }
}
