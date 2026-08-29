using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Regime.Evidence.BaiPerron;
using IQIAIndicator.Engine.Regime.Evidence.CUSUM;

namespace IQIAIndicator.Engine.Fusion.Rules;

/// <summary>
/// Categorical agreement between CUSUM and Bai-Perron on whether a structural break is currently
/// detected. Diagnostic only - never converted to a numeric vote or blended into a score (Lot 15.7 §4/§13).
/// </summary>
public enum StructuralBreakAgreement
{
    /// <summary>Bai-Perron could not be evaluated; agreement cannot be determined.</summary>
    Unavailable,
    Neither,
    CusumOnly,
    BaiPerronOnly,
    Both
}

/// <summary>
/// Sprint 15.25 (Lot 15.8). The minimal StructuralBreak evidence contract decided in Lot 15.7 §19.
/// BreakCountMagnitude/BreakLocationBarIndex/Agreement are diagnostic fields only - they are exposed for
/// observability and future use, but deliberately never feed into Value/Confidence in
/// <see cref="StructuralBreakEvidenceRule"/> (that blending would be a reweighting decision, out of scope
/// for this lot per its brief §25).
/// </summary>
public sealed record StructuralBreakContract
{
    /// <summary>True when CUSUM currently detects a change. Never derived from Bai-Perron (Lot 15.6/15.7:
    /// Bai-Perron's BreakCount&gt;0 is detected in 99.98% of the reference dataset - non-discriminant).</summary>
    public required bool Detected { get; init; }

    /// <summary>Bounded [0,1) structural-break intensity. Sprint 15.25 (Lot 16): revised from the previous
    /// <c>Clamp(cusum.Confidence, 0, 1)</c> - which saturated at exactly 1.0 for every detected bar (Lot
    /// 15.8/15.9) - to <c>r / (1 + r)</c> where <c>r = peakMagnitude / threshold</c> is the pre-clamp
    /// Page-CUSUM ratio (reconstructed from CusumResult fields, not from CusumStatistics). Monotone in r,
    /// parameter-free (s = 0.5 at r = 1, the detection boundary). Still reused as both the scientific score
    /// and the meta-confidence of this dimension (Lot 15.7 §19): CUSUM exposes no second, independent
    /// quality metric, so no blending weight is invented here - the Value/Confidence duplication itself is
    /// out of Lot 16 scope.</summary>
    public required double Strength { get; init; }

    /// <summary>Bai-Perron's BreakCount, taken as-is. 0 when Bai-Perron is unavailable (documented in
    /// Explanation, never conflated with "zero breaks confirmed").</summary>
    public required int BreakCountMagnitude { get; init; }

    /// <summary>Age, in bars, since CUSUM's estimated break point (CurrentBar - BreakLocation, per Lot 15.7
    /// §7's resolution that DetectionTimestamp is always "now" and therefore uninformative). Null when no
    /// break is currently estimated. Diagnostic only - never decayed, never used as a Freshness score.</summary>
    public required int? BreakLocationBarIndex { get; init; }

    public required StructuralBreakAgreement Agreement { get; init; }

    /// <summary>False when CUSUM itself is unavailable/invalid - the whole dimension is then a "we don't
    /// know" sentinel, matching every other Fusion rule's Missing Evidence convention.</summary>
    public required bool IsAvailable { get; init; }

    public required string Explanation { get; init; }
}

/// <summary>
/// Sprint 15.25 (Lot 15.8 - StructuralBreak Evidence Implementation). Exposes CUSUM/Bai-Perron structural
/// break evidence as a new Fusion dimension, per the State-based, Freshness/Persistence-free architecture
/// decided in Lot 15.7: no decay function, no dedicated persistence mechanism - this dimension relies
/// entirely on the existing FusionStateManager EMA+hysteresis for temporal stabilization, exactly like
/// every other dimension.
///
/// Deliberately NOT consumed by any Decision.Rules.IDecisionRule in this lot (brief §9/§25: adding evidence
/// must not implicitly create a strategy or reweight StructuralBreakRule) - it is produced and stabilized,
/// available for a future, explicitly-scoped reweighting lot.
///
/// Sprint 15.25 (Lot 16 - Strength Contract Repair): the continuous <see cref="StructuralBreakContract.Strength"/>
/// field is normalized via <see cref="NormalizeStrength"/> (r / (1 + r)) instead of the old saturating
/// <c>Clamp(cusum.Confidence, 0, 1)</c>. No calibration, no reweighting, no DecisionRule change - see the
/// Lot 16 report. CusumStatistics.Compute is NOT touched (protected, other consumers).
/// </summary>
public sealed class StructuralBreakEvidenceRule : IFusionRule
{
    public string Name => nameof(StructuralBreakEvidenceRule);

    public void Evaluate(FusionContext context, FusionResultBuilder builder)
    {
        StructuralBreakContract contract = BuildContract(context.Evidence.Cusum, context.Evidence.BaiPerron);

        FusionConfidence confidence = contract.IsAvailable
            ? new FusionConfidence
            {
                Value = contract.Strength,
                Confidence = contract.Strength,
                Explanation = contract.Explanation
            }
            : new FusionConfidence
            {
                Value = 0.0,
                Confidence = 0.0,
                Explanation = contract.Explanation,
                IsAvailable = false
            };

        builder.Dimensions[FusionDimension.StructuralBreak] = confidence;
    }

    /// <summary>Pure, directly testable construction of the contract from raw evidence - no FusionContext
    /// dependency, so it can be exercised with hand-built CusumResult/BaiPerronResult fixtures.</summary>
    public static StructuralBreakContract BuildContract(CusumResult? cusum, BaiPerronResult? baiPerron)
    {
        if (cusum is null || !cusum.IsValid)
        {
            return new StructuralBreakContract
            {
                Detected = false,
                Strength = 0.0,
                BreakCountMagnitude = 0,
                BreakLocationBarIndex = null,
                Agreement = StructuralBreakAgreement.Unavailable,
                IsAvailable = false,
                Explanation = "Missing Evidence: CUSUM unavailable or invalid."
            };
        }

        bool baiPerronAvailable = baiPerron is { IsValid: true };
        bool cusumDetected = cusum.ChangeDetected;
        bool baiPerronDetected = baiPerronAvailable && baiPerron!.BreakCount > 0;

        StructuralBreakAgreement agreement = !baiPerronAvailable
            ? StructuralBreakAgreement.Unavailable
            : (cusumDetected, baiPerronDetected) switch
            {
                (true, true) => StructuralBreakAgreement.Both,
                (true, false) => StructuralBreakAgreement.CusumOnly,
                (false, true) => StructuralBreakAgreement.BaiPerronOnly,
                (false, false) => StructuralBreakAgreement.Neither
            };

        int? breakLocationBarIndex = (!cusumDetected || cusum.EstimatedBreakIndex < 0)
            ? null
            : Math.Max(0, cusum.SampleSize - 1 - cusum.EstimatedBreakIndex);

        double strength = NormalizeStrength(cusum);
        int breakCountMagnitude = baiPerronAvailable ? baiPerron!.BreakCount : 0;

        return new StructuralBreakContract
        {
            Detected = cusumDetected,
            Strength = strength,
            BreakCountMagnitude = breakCountMagnitude,
            BreakLocationBarIndex = breakLocationBarIndex,
            Agreement = agreement,
            IsAvailable = true,
            Explanation = BuildExplanation(
                cusumDetected, strength, baiPerronAvailable, breakCountMagnitude, agreement, breakLocationBarIndex)
        };
    }

    /// <summary>
    /// Sprint 15.25 (Lot 16 - Strength Contract Repair). Bounded, monotone, parameter-free normalization
    /// of the pre-clamp Page-CUSUM ratio <c>r = peakMagnitude / threshold</c>, reconstructed here from the
    /// already-exposed <see cref="CusumResult"/> fields (PositiveCusum / NegativeCusum / Threshold) so that
    /// <c>CusumStatistics.Compute</c> - shared, protected production code with other consumers (the
    /// ScientificDashboard, the backtest fingerprint) - is NOT touched.
    ///
    /// The previous contract, <c>Strength = Math.Clamp(cusum.Confidence, 0, 1)</c>, saturated at exactly
    /// 1.0 for every <see cref="CusumResult.ChangeDetected"/> bar: <c>cusum.Confidence</c> is itself
    /// <c>Math.Clamp(r, 0, 1)</c> inside CusumStatistics, and <c>r &gt; 1</c> is the definition of detection
    /// (peakMagnitude &gt; threshold). On the reference Yahoo MES M5 dataset r ranged 1.0 .. 5982.8 with
    /// variance ~20000 among detected bars, all collapsed to the single value 1.0 (Lot 15.8 / 15.9).
    ///
    /// Transform: <c>s = r / (1 + r)</c>.
    ///   - monotone increasing on [0, inf):  ds/dr = 1 / (1 + r)^2 &gt; 0
    ///   - bounded:  s in [0, 1),  s(0) = 0,  s -&gt; 1 as r -&gt; inf  (never exactly 1 for finite r)
    ///   - no calibration parameter: the only constant is the "1" in the denominator, placing
    ///     <c>s = 0.5</c> at <c>r = 1</c> - exactly the Page-CUSUM decision boundary
    ///     (peakMagnitude == threshold). The anchor is the pre-existing detection threshold defined in
    ///     CusumStatistics, not a value tuned for this dimension. Equivalent to logistic(ln r).
    ///
    /// <see cref="StructuralBreakContract.Detected"/> stays a separate boolean; a high Strength is never
    /// the detection flag and is never derived from it.
    /// </summary>
    internal static double NormalizeStrength(CusumResult cusum)
    {
        double peakMagnitude = Math.Max(cusum.PositiveCusum, Math.Abs(cusum.NegativeCusum));
        double rawRatio = cusum.Threshold > 0.0 ? peakMagnitude / cusum.Threshold : 0.0;
        if (!double.IsFinite(rawRatio) || rawRatio <= 0.0)
            return 0.0;

        return rawRatio / (1.0 + rawRatio);
    }

    private static string BuildExplanation(
        bool detected,
        double strength,
        bool baiPerronAvailable,
        int breakCountMagnitude,
        StructuralBreakAgreement agreement,
        int? breakLocationBarIndex) =>
        $"Detected={detected}; Strength={strength:F3} (r/(1+r), r=peakCusum/threshold); " +
        $"BreakCountMagnitude={breakCountMagnitude} ({(baiPerronAvailable ? "Bai-Perron" : "unavailable")}); " +
        $"BreakLocationBarIndex={(breakLocationBarIndex?.ToString() ?? "n/a")} " +
        "(diagnostic age in bars since estimated break, not an absolute series index, never decayed); " +
        $"Agreement={agreement}.";
}
