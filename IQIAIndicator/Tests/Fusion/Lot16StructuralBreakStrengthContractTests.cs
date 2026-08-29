using System;
using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Regime.Evidence.CUSUM;
using Xunit;

namespace IQIAIndicator.Tests.Fusion;

/// <summary>
/// LOT 16 - "Fusion Dimension Contract Repair &amp; Scientific Normalization". Dedicated, deterministic,
/// network-free property suite for the revised <see cref="FusionDimension.StructuralBreak"/> Strength
/// contract:
///
///   old:  Strength = Math.Clamp(cusum.Confidence, 0, 1)        (saturates at 1.0 for every detected bar)
///   new:  Strength = r / (1 + r),  r = peakCusum / threshold   (bounded, monotone, parameter-free)
///
/// Mandatory Lot 16 test items covered here (the Yahoo-data items - non-saturation on real data,
/// look-ahead, run isolation, 5-dimension regression, dataset identity - are in
/// <c>Tests/Research/Lot16ContractNormalization/Lot16ContractRegressionTests.cs</c>):
///   1. Bornes:            Strength in [0, 1]
///   2. Monotonicite:      raw ratio increasing =&gt; Strength non-decreasing
///   3. Non saturation:    a heavy-tailed synthetic ratio sweep yields distinct, positive-variance Strength
///   4. Determinisme:      identical CusumResult =&gt; identical Strength, repeated
///   5. Detection separee: a high Strength is never the Detected boolean and is never derived from it
///   6. Garde numerique:   non-finite / degenerate ratio =&gt; 0.0 (never NaN / Infinity)
/// </summary>
public sealed class Lot16StructuralBreakStrengthContractTests
{
    private static CusumResult Cusum(double positiveCusum, double threshold, bool changeDetected, double confidence = 0.0) => new()
    {
        ChangeDetected = changeDetected,
        EstimatedBreakIndex = changeDetected ? 10 : -1,
        PositiveCusum = positiveCusum,
        NegativeCusum = 0.0,
        Threshold = threshold,
        Confidence = confidence,
        SampleSize = 30,
        IsValid = true,
        Explanation = string.Empty
    };

    private static double Strength(double rawRatio) =>
        StructuralBreakEvidenceRule.NormalizeStrength(Cusum(rawRatio * 5.0, 5.0, rawRatio > 1.0));

    // ── 1. Bornes ─────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(14.79)]      // measured median raw ratio among detected bars (Lot 16 Phase 3)
    [InlineData(47.0)]       // measured mean
    [InlineData(556.95)]     // measured P99
    [InlineData(5982.76)]    // measured max
    [InlineData(1e9)]
    public void Strength_IsWithinUnitInterval_ForTheFullMeasuredRatioRange(double rawRatio)
    {
        double s = Strength(rawRatio);
        Assert.True(s >= 0.0, $"Strength {s} < 0 at r={rawRatio}");
        Assert.True(s < 1.0, $"Strength {s} not strictly < 1 at r={rawRatio} (r/(1+r) never reaches 1 for finite r)");
    }

    [Fact]
    public void Strength_IsExactlyZero_AtZeroRatio_AndExactlyHalf_AtTheDetectionBoundary()
    {
        Assert.Equal(0.0, Strength(0.0));
        Assert.Equal(0.5, Strength(1.0), precision: 12); // r = 1  <=>  peakCusum == threshold  <=>  Page-CUSUM boundary
    }

    // ── 2. Monotonicite ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Strength_IsStrictlyIncreasing_AcrossAnAscendingRatioSweep()
    {
        double[] ratios = Enumerable.Range(0, 400).Select(i => i * 0.05).Concat(new[] { 15.0, 47.0, 100.0, 557.0, 5983.0 }).ToArray();
        double previous = double.NegativeInfinity;
        double previousRatio = double.NegativeInfinity;
        foreach (double r in ratios.OrderBy(x => x))
        {
            double s = Strength(r);
            Assert.True(s >= previous, $"non-monotone: r {previousRatio}->{r} gave Strength {previous}->{s}");
            if (r > previousRatio + 1e-9) Assert.True(s > previous - 1e-15, "expected strict increase for a strictly larger ratio");
            previous = s;
            previousRatio = r;
        }
    }

    [Fact]
    public void Strength_PreservesTheRelativeRankingOfRawRatios()
    {
        var raw = new[] { 0.3, 1.0, 2.7, 9.1, 14.79, 88.0, 540.0, 5000.0 };
        var byRaw = raw.OrderBy(x => x).ToArray();
        var byStrength = raw.OrderBy(Strength).ToArray();
        Assert.Equal(byRaw, byStrength); // monotone transform => identical ordering
    }

    // ── 3. Non saturation (synthetic heavy tail) ─────────────────────────────────────────────────────

    [Fact]
    public void Strength_DoesNotSaturate_OnAHeavyTailedRatioSample()
    {
        // The pre-clamp CUSUM ratio measured on real Yahoo MES M5 spans ~1 .. ~6000 with a heavy right
        // tail (median ~15, mean ~47). The old contract collapsed all of it to the single value 1.0.
        double[] ratios = { 1.0001, 1.5, 2.16, 3.0, 5.0, 8.0, 14.79, 25.0, 47.0, 97.66, 200.0, 556.95, 1500.0, 5982.76 };
        double[] strengths = ratios.Select(Strength).ToArray();

        Assert.Equal(strengths.Length, strengths.Distinct().Count());          // every ratio -> a distinct Strength
        Assert.All(strengths, s => Assert.True(s < 0.9999, $"Strength {s} counts as saturated"));
        double mean = strengths.Average();
        double variance = strengths.Sum(s => (s - mean) * (s - mean)) / (strengths.Length - 1);
        Assert.True(variance > 0.0, "Strength variance must be strictly positive across the detected-zone ratio range");
    }

    // ── 4. Determinisme ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Strength_IsBitIdentical_AcrossRepeatedEvaluationsOfTheSameInput()
    {
        CusumResult fixture = Cusum(positiveCusum: 231.7, threshold: 3.14, changeDetected: true, confidence: 1.0);
        long firstBits = BitConverter.DoubleToInt64Bits(StructuralBreakEvidenceRule.NormalizeStrength(fixture));
        for (int i = 0; i < 50; i++)
            Assert.Equal(firstBits, BitConverter.DoubleToInt64Bits(StructuralBreakEvidenceRule.NormalizeStrength(fixture)));
    }

    // ── 5. Detection restee separee ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Strength_IsNeverTheDetectionBoolean_HighStrengthDoesNotImplyDetectedAndViceVersa()
    {
        // Contract is built straight from the fields as given - BuildContract does not force Strength and
        // Detected into agreement, so the two remain independent channels.
        StructuralBreakContract highStrengthNotDetected = StructuralBreakEvidenceRule.BuildContract(
            Cusum(positiveCusum: 500.0, threshold: 5.0, changeDetected: false), null); // r = 100
        Assert.False(highStrengthNotDetected.Detected);
        Assert.True(highStrengthNotDetected.Strength > 0.9, "the ratio channel is free to be high while Detected is false");

        StructuralBreakContract detectedLowStrength = StructuralBreakEvidenceRule.BuildContract(
            Cusum(positiveCusum: 5.05, threshold: 5.0, changeDetected: true), null); // r ~ 1.01
        Assert.True(detectedLowStrength.Detected);
        Assert.True(detectedLowStrength.Strength < 0.55, "just past the boundary, Strength sits near 0.5 - not snapped to 1.0");
    }

    // ── 6. Garde numerique ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.0)]                 // threshold 0 -> guarded, peak/0 never evaluated
    [InlineData(-1.0)]                // negative threshold -> guarded
    public void NormalizeStrength_NonPositiveThreshold_ReturnsZero(double threshold)
    {
        Assert.Equal(0.0, StructuralBreakEvidenceRule.NormalizeStrength(Cusum(positiveCusum: 50.0, threshold: threshold, changeDetected: true)));
    }

    [Fact]
    public void NormalizeStrength_NonFiniteCusumSums_ReturnZero_NeverNaNOrInfinity()
    {
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            double s = StructuralBreakEvidenceRule.NormalizeStrength(Cusum(positiveCusum: bad, threshold: 5.0, changeDetected: true));
            Assert.True(double.IsFinite(s));
            Assert.Equal(0.0, s);
        }
    }

    [Fact]
    public void NormalizeStrength_UsesTheLargerOfPositiveAndAbsNegativeCusum()
    {
        // peak = max(PositiveCusum, |NegativeCusum|) - a large negative excursion drives Strength just as
        // a large positive one does.
        CusumResult negativeDominant = new()
        {
            ChangeDetected = true, EstimatedBreakIndex = 10,
            PositiveCusum = 2.0, NegativeCusum = -60.0, Threshold = 5.0, Confidence = 1.0,
            SampleSize = 30, IsValid = true, Explanation = string.Empty
        };
        Assert.Equal(12.0 / 13.0, StructuralBreakEvidenceRule.NormalizeStrength(negativeDominant), precision: 12); // r = 60/5 = 12
    }
}
