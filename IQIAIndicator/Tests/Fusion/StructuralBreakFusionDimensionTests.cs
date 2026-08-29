using System;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Regime.Evidence.ADF;
using IQIAIndicator.Engine.Regime.Evidence.BaiPerron;
using IQIAIndicator.Engine.Regime.Evidence.CUSUM;
using IQIAIndicator.Engine.Regime.Evidence.DFA;
using IQIAIndicator.Engine.Regime.Evidence.HalfLife;
using IQIAIndicator.Engine.Regime.Evidence.KPSS;
using IQIAIndicator.Engine.Regime.Evidence.VarianceRatio;
using Xunit;
using FusionEngine = IQIAIndicator.Engine.Fusion.EvidenceFusionEngine;

namespace IQIAIndicator.Tests.Fusion;

/// <summary>
/// Sprint 15.25 (Lot 15.8). Confirms (1) <see cref="FusionDimension.StructuralBreak"/> exists as an
/// additive enum value, (2) <see cref="EvidenceFusionEngine.Fuse"/> with
/// <see cref="StructuralBreakEvidenceRule"/> in its rule list produces a
/// <see cref="FusionResult.Dimensions"/> entry for it, and (3) adding that rule to the list never changes
/// any of the five pre-existing dimensions' values, on the SAME <see cref="FusionContext"/> - direct proof
/// that <see cref="StructuralBreakEvidenceRule"/> only ever writes its own dimension key and never reads
/// or perturbs another rule's output (each <see cref="IFusionRule"/> only writes to
/// <c>builder.Dimensions[itsOwnKey]</c> - see <see cref="EvidenceFusionEngine.Fuse"/>'s simple
/// foreach-and-write loop).
/// </summary>
public sealed class StructuralBreakFusionDimensionTests
{
    [Fact]
    public void FusionDimension_StructuralBreak_IsDefined()
    {
        Assert.True(Enum.IsDefined(typeof(FusionDimension), FusionDimension.StructuralBreak));
    }

    [Fact]
    public void Fuse_WithStructuralBreakEvidenceRule_ProducesStructuralBreakDimensionEntry()
    {
        var engine = new FusionEngine(new IFusionRule[]
        {
            new StationarityRule(), new PersistenceRule(), new MeanReversionRule(), new RandomWalkRule(),
            new StructuralBreakEvidenceRule()
        });

        FusionResult result = engine.Fuse(FullContext());

        Assert.True(result.Dimensions.ContainsKey(FusionDimension.StructuralBreak));
        Assert.True(result.Dimensions[FusionDimension.StructuralBreak].IsAvailable);
    }

    [Fact]
    public void AddingStructuralBreakEvidenceRule_NeverChangesTheOtherFiveDimensions()
    {
        FusionContext context = FullContext();

        var withoutRule = new FusionEngine(new IFusionRule[]
        {
            new StationarityRule(), new PersistenceRule(), new MeanReversionRule(), new RandomWalkRule()
        });
        var withRule = new FusionEngine(new IFusionRule[]
        {
            new StationarityRule(), new PersistenceRule(), new MeanReversionRule(), new RandomWalkRule(),
            new StructuralBreakEvidenceRule()
        });

        FusionResult before = withoutRule.Fuse(context);
        FusionResult after = withRule.Fuse(context);

        Assert.False(before.Dimensions.ContainsKey(FusionDimension.StructuralBreak));
        Assert.True(after.Dimensions.ContainsKey(FusionDimension.StructuralBreak));

        foreach (FusionDimension dimension in new[]
        {
            FusionDimension.Stationarity, FusionDimension.Persistence, FusionDimension.MeanReversion, FusionDimension.RandomWalk
        })
        {
            FusionConfidence b = before.Dimensions[dimension];
            FusionConfidence a = after.Dimensions[dimension];

            Assert.Equal(b.Value, a.Value);
            Assert.Equal(b.Confidence, a.Confidence);
            Assert.Equal(b.Explanation, a.Explanation);
            Assert.Equal(b.IsAvailable, a.IsAvailable);
        }

        // StructuralStability is computed downstream by FusionStateManager/StructuralStabilityRule from a
        // profile history, not by any IFusionRule in this rule list - neither engine here produces it at
        // all, which is itself consistent with the dimension being untouched by this rule addition.
        Assert.False(before.Dimensions.ContainsKey(FusionDimension.StructuralStability));
        Assert.False(after.Dimensions.ContainsKey(FusionDimension.StructuralStability));
    }

    // ──────────────────────────────────────────────── Helpers ─────────────────────────────────────────────

    private static FusionContext FullContext() => new()
    {
        Evidence = new EvidenceSet
        {
            Timestamp = DateTime.UnixEpoch,
            Adf = new AdfResult
            {
                Statistic = -3.5m, PValue = 0.01m, Confidence = 1m,
                CriticalValue1 = -3.5m, CriticalValue5 = -2.9m, CriticalValue10 = -2.6m,
                IsStationary = true, LagUsed = 2, SampleSize = 100, Explanation = string.Empty, IsValid = true
            },
            Kpss = new KpssResult
            {
                Statistic = 0.1m, PValue = 0.09m, Confidence = 1m,
                CriticalValue1 = 0.74m, CriticalValue5 = 0.46m, CriticalValue10 = 0.35m,
                IsStationary = true, Bandwidth = 4, SampleSize = 100, Explanation = string.Empty, IsValid = true
            },
            Hurst = null,
            HalfLife = new HalfLifeResult
            {
                HalfLife = 5.0, Lambda = -0.1, Intercept = 0.0, StandardError = 0.1,
                RSquared = 0.9, Confidence = 0.9, SampleSize = 200, IsValid = true, Explanation = string.Empty
            },
            VarianceRatio = new VarianceRatioResult
            {
                VarianceRatio = 1.6, ZStatistic = 3.5, PValue = 0.001, Confidence = 0.95,
                Lag = 5, SampleSize = 200, IsValid = true, Explanation = string.Empty
            },
            Cusum = new CusumResult
            {
                ChangeDetected = true, EstimatedBreakIndex = 20, PositiveCusum = 6.0, NegativeCusum = 0.0,
                Threshold = 5.0, Confidence = 0.7, SampleSize = 30, IsValid = true, Explanation = string.Empty
            },
            Volatility = null,
            BaiPerron = new BaiPerronResult
            {
                Breakpoints = new[] { 100 }, BreakCount = 1, Confidence = 0.6,
                GlobalRSS = 1.0, BicScore = 1.0, SampleSize = 128, IsValid = true, Explanation = string.Empty
            },
            Dfa = new DfaResult
            {
                Hurst = 0.65, RSquared = 0.9, Confidence = 0.9, WindowCount = 10, IsValid = true, Explanation = string.Empty
            }
        },
        Timestamp = DateTime.UnixEpoch,
        Symbol = "TEST",
        TimeFrame = "M5",
        EvaluationId = Guid.Empty
    };
}
