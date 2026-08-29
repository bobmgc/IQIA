using System;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Regime.Evidence.BaiPerron;
using IQIAIndicator.Engine.Regime.Evidence.CUSUM;
using Xunit;

namespace IQIAIndicator.Tests.Fusion;

/// <summary>
/// Sprint 15.25 (Lot 15.8, "StructuralBreak Evidence Implementation"; Strength assertions revised in
/// Lot 16 - "Strength Contract Repair" - where Strength moved from the saturating
/// <c>Clamp(cusum.Confidence, 0, 1)</c> to the bounded monotone <c>r / (1 + r)</c>,
/// <c>r = peakCusum / threshold</c>). Contract tests for
/// <see cref="StructuralBreakEvidenceRule.BuildContract"/> (pure, directly testable) and
/// <see cref="StructuralBreakEvidenceRule.Evaluate"/>. Mirrors the fixture-construction style of
/// <c>StationarityRuleTests</c>/<c>PersistenceRuleTests</c>/<c>MeanReversionRuleTests</c>/
/// <c>RandomWalkRuleTests</c> (Tests/Fusion), but written as plain xunit [Fact]s directly - the
/// convention every Lot 15.x test file added to this repo already uses (no RunAll()/XunitWrappers
/// indirection for new files).
/// </summary>
public sealed class StructuralBreakEvidenceRuleTests
{
    // ─────────────────────────────────────── BuildContract: missing evidence ──────────────────────────────

    [Fact]
    public void BuildContract_NullCusum_IsMissingEvidence_DetectedNeverTrue()
    {
        StructuralBreakContract contract = StructuralBreakEvidenceRule.BuildContract(null, ValidBaiPerron(breakCount: 3));

        Assert.False(contract.IsAvailable);
        Assert.False(contract.Detected);
        Assert.Equal(0.0, contract.Strength);
        Assert.Equal(0, contract.BreakCountMagnitude);
        Assert.Null(contract.BreakLocationBarIndex);
        Assert.Equal(StructuralBreakAgreement.Unavailable, contract.Agreement);
        Assert.Contains("Missing Evidence", contract.Explanation);
    }

    [Fact]
    public void BuildContract_InvalidCusum_IsMissingEvidence_DetectedNeverTrue()
    {
        // CusumResult.Invalid() itself sets ChangeDetected=false, but the point of this test is that
        // BuildContract takes the "IsAvailable=false" branch entirely on IsValid==false - even if some
        // hypothetical invalid CusumResult carried ChangeDetected=true, the contract must still report
        // Detected=false (verified below by constructing exactly that hypothetical).
        CusumResult invalidButFlagged = CusumResult.Invalid("Warmup: below MinimumSampleSize", sampleSize: 10) with
        {
            ChangeDetected = true // hypothetical malformed input - IsValid=false must still win
        };

        StructuralBreakContract contract = StructuralBreakEvidenceRule.BuildContract(invalidButFlagged, ValidBaiPerron(breakCount: 1));

        Assert.False(contract.IsAvailable);
        Assert.False(contract.Detected, "An invalid CUSUM result must never yield Detected=true, even if ChangeDetected was somehow set.");
        Assert.Equal(0.0, contract.Strength);
        Assert.Null(contract.BreakLocationBarIndex);
        Assert.Equal(StructuralBreakAgreement.Unavailable, contract.Agreement);
    }

    [Fact]
    public void BuildContract_WarmupSentinels_NeverProduceDetectedTrue()
    {
        // Historique insuffisant: exactly what RegimeEngine hands back during warmup for both evidences.
        CusumResult cusumWarmup = CusumResult.Invalid("Warmup: SampleSize 10 < MinimumSampleSize 20", sampleSize: 10);
        BaiPerronResult baiPerronWarmup = BaiPerronResult.Invalid("Warmup: SampleSize 30 < MinimumSampleSize 64", sampleSize: 30);

        StructuralBreakContract contract = StructuralBreakEvidenceRule.BuildContract(cusumWarmup, baiPerronWarmup);

        Assert.False(contract.IsAvailable);
        Assert.False(contract.Detected);
        Assert.Equal(0.0, contract.Strength);
        Assert.Equal(0, contract.BreakCountMagnitude);
        Assert.Null(contract.BreakLocationBarIndex);
        Assert.Equal(StructuralBreakAgreement.Unavailable, contract.Agreement);
    }

    // ─────────────────────────────── BuildContract: Cusum valid, Bai-Perron missing/invalid ─────────────────

    [Fact]
    public void BuildContract_ValidCusumDetected_NullBaiPerron_DetectedAndStrengthValid_AgreementUnavailable()
    {
        // Lot 16: Strength = r / (1 + r), r = peakCusum / threshold = 20 / 5 = 4  =>  4/5 = 0.8.
        CusumResult cusum = ValidCusum(changeDetected: true, confidence: 0.8, sampleSize: 30, estimatedBreakIndex: 20,
            positiveCusum: 20.0, threshold: 5.0);

        StructuralBreakContract contract = StructuralBreakEvidenceRule.BuildContract(cusum, null);

        Assert.True(contract.IsAvailable);
        Assert.True(contract.Detected);
        Assert.Equal(4.0 / 5.0, contract.Strength, precision: 12);
        Assert.Equal(0, contract.BreakCountMagnitude); // BaiPerron unavailable => magnitude is 0, never fabricated.
        Assert.Equal(StructuralBreakAgreement.Unavailable, contract.Agreement);
        Assert.NotNull(contract.BreakLocationBarIndex);
    }

    [Fact]
    public void BuildContract_ValidCusumDetected_InvalidBaiPerron_AgreementUnavailable_MagnitudeZero()
    {
        // Lot 16: r = 15 / 5 = 3  =>  3/4 = 0.75.
        CusumResult cusum = ValidCusum(changeDetected: true, confidence: 0.65, sampleSize: 30, estimatedBreakIndex: 27,
            positiveCusum: 15.0, threshold: 5.0);
        BaiPerronResult invalidBaiPerron = BaiPerronResult.Invalid("Warmup", sampleSize: 30) with { BreakCount = 5 }; // hypothetical malformed input

        StructuralBreakContract contract = StructuralBreakEvidenceRule.BuildContract(cusum, invalidBaiPerron);

        Assert.True(contract.IsAvailable);
        Assert.True(contract.Detected);
        Assert.Equal(3.0 / 4.0, contract.Strength, precision: 12);
        Assert.Equal(0, contract.BreakCountMagnitude); // Invalid BaiPerron must never leak its raw BreakCount into the magnitude.
        Assert.Equal(StructuralBreakAgreement.Unavailable, contract.Agreement);
    }

    // ───────────────────────────────────────── BuildContract: Agreement matrix ────────────────────────────

    [Theory]
    [InlineData(true, 2, StructuralBreakAgreement.Both)]
    [InlineData(true, 0, StructuralBreakAgreement.CusumOnly)]
    [InlineData(false, 3, StructuralBreakAgreement.BaiPerronOnly)]
    [InlineData(false, 0, StructuralBreakAgreement.Neither)]
    public void BuildContract_AgreementMatrix_MatchesCusumDetectedAndBaiPerronBreakCount(
        bool cusumChangeDetected, int baiPerronBreakCount, StructuralBreakAgreement expectedAgreement)
    {
        CusumResult cusum = ValidCusum(changeDetected: cusumChangeDetected, confidence: 0.5, sampleSize: 30, estimatedBreakIndex: cusumChangeDetected ? 15 : -1);
        BaiPerronResult baiPerron = ValidBaiPerron(breakCount: baiPerronBreakCount);

        StructuralBreakContract contract = StructuralBreakEvidenceRule.BuildContract(cusum, baiPerron);

        Assert.Equal(expectedAgreement, contract.Agreement);
        Assert.Equal(cusumChangeDetected, contract.Detected);
        Assert.Equal(baiPerronBreakCount, contract.BreakCountMagnitude);
    }

    // ───────────────────────── BuildContract: Strength (Lot 16 — r/(1+r) soft saturation) ─────────────────

    [Fact]
    public void BuildContract_ZeroRatioCusum_ProducesZeroStrength()
    {
        // peakCusum = 0 => r = 0 => Strength = 0/(1+0) = 0.
        CusumResult cusum = ValidCusum(changeDetected: false, confidence: 0.0, sampleSize: 30, estimatedBreakIndex: -1,
            positiveCusum: 0.0, threshold: 5.0);

        StructuralBreakContract contract = StructuralBreakEvidenceRule.BuildContract(cusum, ValidBaiPerron(0));

        Assert.Equal(0.0, contract.Strength);
        Assert.False(contract.Detected);
    }

    [Fact]
    public void BuildContract_ValidDetectedCusum_StrengthIsSoftSaturatedRawRatio_NotCusumConfidence()
    {
        // Lot 16: Strength = r/(1+r), r = peakCusum/threshold, reconstructed from CusumResult fields.
        // cusum.Confidence (itself the saturating Clamp(r,0,1)) is deliberately NOT read any more.
        // r = 35 / 5 = 7  =>  7/8 = 0.875, regardless of the Confidence value carried on the fixture.
        CusumResult cusum = ValidCusum(changeDetected: true, confidence: 0.734, sampleSize: 30, estimatedBreakIndex: 10,
            positiveCusum: 35.0, threshold: 5.0);

        StructuralBreakContract contract = StructuralBreakEvidenceRule.BuildContract(cusum, ValidBaiPerron(1));

        Assert.Equal(7.0 / 8.0, contract.Strength, precision: 12);
        Assert.True(contract.Detected);
    }

    [Theory]
    [InlineData(0.0, 0.0)]                       // r = 0
    [InlineData(5.0, 0.5)]                       // r = 1  (Page-CUSUM detection boundary) -> 0.5
    [InlineData(50.0, 10.0 / 11.0)]              // r = 10
    [InlineData(5_000.0, 1_000.0 / 1_001.0)]     // r = 1000 - still strictly < 1, never saturates
    public void BuildContract_Strength_IsBoundedSoftSaturationOfRawRatio(double positiveCusum, double expected)
    {
        CusumResult cusum = ValidCusum(changeDetected: positiveCusum > 5.0, confidence: 0.0, sampleSize: 30,
            estimatedBreakIndex: 10, positiveCusum: positiveCusum, threshold: 5.0);

        StructuralBreakContract contract = StructuralBreakEvidenceRule.BuildContract(cusum, null);

        Assert.Equal(expected, contract.Strength, precision: 12);
        Assert.InRange(contract.Strength, 0.0, 1.0);
    }

    [Fact]
    public void BuildContract_NaNCusumConfidence_NoLongerReachesStrength_Lot16()
    {
        // Lot 16 improvement: Strength is computed from PositiveCusum/NegativeCusum/Threshold, never from
        // cusum.Confidence, so a NaN in Confidence can no longer propagate into the contract. (A non-finite
        // raw ratio - e.g. threshold 0 - is additionally guarded to 0.0 inside NormalizeStrength.)
        CusumResult cusum = ValidCusum(changeDetected: true, confidence: double.NaN, sampleSize: 30, estimatedBreakIndex: 10,
            positiveCusum: 20.0, threshold: 5.0); // r = 4

        StructuralBreakContract contract = StructuralBreakEvidenceRule.BuildContract(cusum, null);

        Assert.Equal(4.0 / 5.0, contract.Strength, precision: 12);
        Assert.True(double.IsFinite(contract.Strength));
    }

    [Fact]
    public void NormalizeStrength_NonFiniteOrDegenerateRatio_IsGuardedToZero()
    {
        // threshold 0 => rawRatio computed as 0.0 by the guard; peak/0 never evaluated.
        CusumResult zeroThreshold = ValidCusum(changeDetected: true, confidence: 0.9, sampleSize: 30,
            estimatedBreakIndex: 10, positiveCusum: 50.0, threshold: 0.0);
        Assert.Equal(0.0, StructuralBreakEvidenceRule.NormalizeStrength(zeroThreshold));

        // NaN in the cusum sums => non-finite ratio => guarded to 0.0.
        CusumResult nanPeak = ValidCusum(changeDetected: true, confidence: 0.9, sampleSize: 30,
            estimatedBreakIndex: 10, positiveCusum: double.NaN, threshold: 5.0);
        Assert.Equal(0.0, StructuralBreakEvidenceRule.NormalizeStrength(nanPeak));
    }

    [Fact]
    public void NormalizeStrength_IsMonotoneNonDecreasing_InRawRatio()
    {
        double[] ratios = { 0.0, 0.25, 0.5, 0.999, 1.0, 1.001, 2, 5, 14.79, 47, 100, 556, 5982.76 };
        double previous = double.NegativeInfinity;
        foreach (double r in ratios)
        {
            CusumResult cusum = ValidCusum(changeDetected: r > 1.0, confidence: 0.0, sampleSize: 30,
                estimatedBreakIndex: 10, positiveCusum: r * 5.0, threshold: 5.0);
            double s = StructuralBreakEvidenceRule.NormalizeStrength(cusum);
            Assert.InRange(s, 0.0, 1.0);
            Assert.True(s >= previous, $"non-monotone at r={r}: {s} < {previous}");
            previous = s;
        }
        Assert.True(previous < 1.0, "r/(1+r) must stay strictly below 1 for every finite ratio");
    }

    [Fact]
    public void BuildContract_BaiPerronNumericAnomalies_NeverReadIntoContract()
    {
        // BaiPerronResult.GlobalRSS/BicScore/Confidence being NaN/Infinity is physically constructible
        // (plain doubles, no invariant) but BuildContract only ever reads BaiPerron.IsValid and
        // BaiPerron.BreakCount (an int, which cannot carry NaN/Infinity) - so such anomalies structurally
        // cannot reach StructuralBreakContract at all. Verified directly here.
        CusumResult cusum = ValidCusum(changeDetected: true, confidence: 0.5, sampleSize: 30, estimatedBreakIndex: 10);
        BaiPerronResult anomalous = ValidBaiPerron(breakCount: 2) with
        {
            Confidence = double.NaN,
            GlobalRSS = double.PositiveInfinity,
            BicScore = double.NegativeInfinity
        };

        StructuralBreakContract contract = StructuralBreakEvidenceRule.BuildContract(cusum, anomalous);

        Assert.True(double.IsFinite(contract.Strength));
        Assert.Equal(2, contract.BreakCountMagnitude);
        Assert.Equal(StructuralBreakAgreement.Both, contract.Agreement);
    }

    // ────────────────────────────────────── BuildContract: BreakLocationBarIndex ──────────────────────────

    [Fact]
    public void BuildContract_EstimatedBreakIndexNegativeOne_BreakLocationIsNull()
    {
        CusumResult cusum = ValidCusum(changeDetected: true, confidence: 0.5, sampleSize: 30, estimatedBreakIndex: -1);

        StructuralBreakContract contract = StructuralBreakEvidenceRule.BuildContract(cusum, null);

        Assert.True(contract.Detected);
        Assert.Null(contract.BreakLocationBarIndex);
    }

    [Fact]
    public void BuildContract_NotDetected_BreakLocationIsNull_RegardlessOfEstimatedBreakIndex()
    {
        // !cusumDetected short-circuits the formula even if EstimatedBreakIndex happens to carry a
        // stale/leftover non-negative value.
        CusumResult cusum = ValidCusum(changeDetected: false, confidence: 0.1, sampleSize: 30, estimatedBreakIndex: 12);

        StructuralBreakContract contract = StructuralBreakEvidenceRule.BuildContract(cusum, null);

        Assert.False(contract.Detected);
        Assert.Null(contract.BreakLocationBarIndex);
    }

    [Fact]
    public void BuildContract_NormalCase_BreakLocationIsSampleSizeMinusOneMinusEstimatedBreakIndex()
    {
        CusumResult cusum = ValidCusum(changeDetected: true, confidence: 0.9, sampleSize: 30, estimatedBreakIndex: 25);

        StructuralBreakContract contract = StructuralBreakEvidenceRule.BuildContract(cusum, null);

        Assert.Equal(30 - 1 - 25, contract.BreakLocationBarIndex); // = 4
    }

    [Fact]
    public void BuildContract_BreakAtTrailingEdge_BreakLocationIsZero()
    {
        CusumResult cusum = ValidCusum(changeDetected: true, confidence: 0.9, sampleSize: 30, estimatedBreakIndex: 29);

        StructuralBreakContract contract = StructuralBreakEvidenceRule.BuildContract(cusum, null);

        Assert.Equal(0, contract.BreakLocationBarIndex);
    }

    [Fact]
    public void BuildContract_EstimatedBreakIndexEqualsSampleSize_AnomalyIsClampedToZero_NeverNegative()
    {
        // Lot 15.6 audit finding: CusumStatistics' own bookkeeping can legitimately emit
        // EstimatedBreakIndex == SampleSize (a "candidate reset index" edge case). Plugged into the raw
        // formula (SampleSize-1-EstimatedBreakIndex) this would be -1; Math.Max(0, ...) clamps it to 0
        // rather than emitting a negative "age".
        CusumResult cusum = ValidCusum(changeDetected: true, confidence: 0.9, sampleSize: 30, estimatedBreakIndex: 30);

        StructuralBreakContract contract = StructuralBreakEvidenceRule.BuildContract(cusum, null);

        Assert.Equal(0, contract.BreakLocationBarIndex);
    }

    // ─────────────────────────────────────────────── Evaluate ─────────────────────────────────────────────

    [Fact]
    public void Evaluate_MissingEvidence_SetsValueAndConfidenceToZero_IsAvailableFalse()
    {
        FusionConfidence confidence = Evaluate(null, null);

        Assert.Equal(0.0, confidence.Value);
        Assert.Equal(0.0, confidence.Confidence);
        Assert.False(confidence.IsAvailable);
        Assert.Contains("Missing Evidence", confidence.Explanation);
    }

    [Fact]
    public void Evaluate_AvailableEvidence_ValueAndConfidenceBothEqualNormalizedStrength_IsAvailableTrue()
    {
        // Lot 16: Value and Confidence still carry the SAME number (duplication out of Lot 16 scope), but
        // that number is now the normalized Strength r/(1+r), r = 30/5 = 6  =>  6/7.
        CusumResult cusum = ValidCusum(changeDetected: true, confidence: 0.62, sampleSize: 30, estimatedBreakIndex: 18,
            positiveCusum: 30.0, threshold: 5.0);
        BaiPerronResult baiPerron = ValidBaiPerron(breakCount: 1);

        FusionConfidence confidence = Evaluate(cusum, baiPerron);

        Assert.Equal(6.0 / 7.0, confidence.Value, precision: 12);
        Assert.Equal(6.0 / 7.0, confidence.Confidence, precision: 12);
        Assert.True(confidence.IsAvailable);
        Assert.Contains("Detected=True", confidence.Explanation);
        Assert.Contains("Agreement=Both", confidence.Explanation);
    }

    [Fact]
    public void Evaluate_WritesToStructuralBreakDimensionKey_NoOtherKey()
    {
        var builder = new FusionResultBuilder();
        new StructuralBreakEvidenceRule().Evaluate(ContextFor(ValidCusum(true, 0.5, 30, 10), null), builder);

        FusionResult result = builder.Build();
        Assert.True(result.Dimensions.ContainsKey(FusionDimension.StructuralBreak));
        Assert.Single(result.Dimensions);
    }

    // ──────────────────────────────────────────────── Helpers ─────────────────────────────────────────────

    private static FusionConfidence Evaluate(CusumResult? cusum, BaiPerronResult? baiPerron)
    {
        var builder = new FusionResultBuilder();
        new StructuralBreakEvidenceRule().Evaluate(ContextFor(cusum, baiPerron), builder);
        return builder.Dimensions[FusionDimension.StructuralBreak];
    }

    private static FusionContext ContextFor(CusumResult? cusum, BaiPerronResult? baiPerron) => new()
    {
        Evidence = new EvidenceSet
        {
            Timestamp = DateTime.UnixEpoch,
            Adf = null,
            Kpss = null,
            Hurst = null,
            HalfLife = null,
            VarianceRatio = null,
            Cusum = cusum,
            Volatility = null,
            BaiPerron = baiPerron,
            Dfa = null
        },
        Timestamp = DateTime.UnixEpoch,
        Symbol = string.Empty,
        TimeFrame = string.Empty,
        EvaluationId = Guid.Empty
    };

    private static CusumResult ValidCusum(
        bool changeDetected, double confidence, int sampleSize, int estimatedBreakIndex,
        double positiveCusum = 0.0, double threshold = 5.0) => new()
    {
        ChangeDetected = changeDetected,
        EstimatedBreakIndex = estimatedBreakIndex,
        PositiveCusum = positiveCusum,
        NegativeCusum = 0.0,
        Threshold = threshold,
        Confidence = confidence, // Lot 16: no longer read by BuildContract; kept because CusumResult requires it.
        SampleSize = sampleSize,
        IsValid = true,
        Explanation = string.Empty
    };

    private static BaiPerronResult ValidBaiPerron(int breakCount) => new()
    {
        Breakpoints = breakCount > 0 ? new[] { 64 } : Array.Empty<int>(),
        BreakCount = breakCount,
        Confidence = breakCount > 0 ? 0.7 : 0.0,
        GlobalRSS = 1.0,
        BicScore = 1.0,
        SampleSize = 128,
        IsValid = true,
        Explanation = string.Empty
    };
}
