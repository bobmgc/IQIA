using IQIAIndicator.Backtest;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Regime.Evidence.BaiPerron;
using IQIAIndicator.Engine.Regime.Evidence.CUSUM;

namespace IQIAIndicator.Tests.Research.StructuralBreakInformationAudit.Support;

/// <summary>
/// Lot 15.9 ("StructuralBreak Evidence Quality &amp; Information Content Audit"). One bar's worth of the
/// central observation table described in the brief's Section A - built once (per dataset load) by
/// <see cref="StructuralBreakObservationSetBuilder"/> and shared, read-only, by every audit test class in
/// this folder. AUDIT-ONLY: nothing here is written back to production, and every field is either read
/// verbatim from the real pipeline's output or a pure recomputation via
/// <see cref="StructuralBreakEvidenceRule.BuildContract"/> / a production-equivalent
/// <see cref="Engine.Fusion.EvidenceFusionEngine"/> + <see cref="Engine.Fusion.State.FusionStateManager"/>.
/// </summary>
internal sealed class ObservationRecord
{
    public required int BarIndex { get; init; }

    public required DateTime Timestamp { get; init; }

    public required MarketState Winner { get; init; }

    public required CusumResult? Cusum { get; init; }

    public required BaiPerronResult? BaiPerron { get; init; }

    public required StructuralBreakContract Contract { get; init; }

    /// <summary>Raw (pre-<see cref="Engine.Fusion.State.FusionStateManager"/>) <c>FusionConfidence.Value</c>
    /// per dimension, indexed by the position of that dimension in
    /// <see cref="StructuralBreakObservationSetBuilder.AllDimensions"/>. StructuralStability's slot is a
    /// documented sentinel (0.0/unavailable) - see the builder's doc comment - because that dimension is
    /// never produced by the production-equivalent 5-rule <c>EvidenceFusionEngine</c> used here (it is
    /// computed separately, post-hoc, inside FusionStateManager.Update itself).</summary>
    public required double[] RawValue { get; init; }

    public required bool[] RawAvailable { get; init; }

    /// <summary>Post-<see cref="Engine.Fusion.State.FusionStateManager"/> (EMA+hysteresis) stabilized
    /// <c>FusionConfidence.Value</c> per dimension, same indexing as <see cref="RawValue"/>. Always a
    /// genuine measurement for every dimension including StructuralStability (FusionStateManager always
    /// populates it).</summary>
    public required double[] StableValue { get; init; }

    public required bool[] StableAvailable { get; init; }
}

/// <summary>Everything one Lot 15.9 test needs about one dataset load: the raw series (for its
/// fingerprint/identity), the real pipeline result (kept so the determinism check can rebuild the
/// observation set a second time without a second network call), and the built observation table.</summary>
internal sealed record AuditDataset(
    HistoricalSeries Series,
    string Fingerprint,
    BacktestSignalPipelineResult PipelineResult,
    IReadOnlyList<ObservationRecord> Observations);
