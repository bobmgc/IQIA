using System.Collections.Generic;

namespace IQIAIndicator.Backtest.Risk;

/// <summary>
/// Sprint 15.25 (Lot 14.8, brief §9). Position eligibility result - what
/// <see cref="PositionRiskEvaluator.Evaluate"/> produces for ONE candidate position. Brief §9's minimum
/// field list (IsAllowed/RequestedQuantity/AllowedQuantity/RiskPerUnit/TotalEstimatedRisk/
/// MaximumAllowedRisk/Reason) plus two diagnostic fields making the "Risk limit -&gt; Quantity limit -&gt;
/// Exposure limit" priority order (brief §10) independently inspectable:
/// <see cref="RiskConstrainedQuantity"/> (what <c>Engine.Risk.RiskEngine</c> alone would allow) and
/// <see cref="ExposureConstrainedQuantity"/> (what <see cref="MaxExposure"/> alone would allow) - so a test
/// can verify each constraint's individual contribution, not just the combined <see cref="AllowedQuantity"/>.
///
/// <see cref="AllowedQuantity"/> is the FINAL, fully-constrained quantity - brief §11's own worked examples
/// ("Requested=10, Risk allows=6, Exposure allows=8 -&gt; Final=6") describe exactly this value, combining
/// <see cref="RequestedQuantity"/>, <see cref="RiskConstrainedQuantity"/> and
/// <see cref="ExposureConstrainedQuantity"/> via <c>min(...)</c>. Never negative; 0 means rejected (brief
/// §11: "Si FinalQuantity &lt;= 0, aucune position ne doit être exécutée").
///
/// Every nullable field mirrors <c>TradePlan</c>/<c>RiskAssessment</c>'s own convention: null means "not
/// evaluated" (e.g. risk controls disabled, or exposure unconfigured), never a fabricated zero.
/// </summary>
public sealed record RiskEvaluationResult(
    bool IsAllowed,
    int RequestedQuantity,
    int AllowedQuantity,
    int? RiskConstrainedQuantity,
    int? ExposureConstrainedQuantity,
    decimal? RiskPerUnit,
    decimal? TotalEstimatedRisk,
    decimal? MaximumAllowedRisk,
    decimal? Exposure,
    decimal? MaxExposure,
    PositionRiskReason Reason,
    IReadOnlyList<string> Diagnostics);
