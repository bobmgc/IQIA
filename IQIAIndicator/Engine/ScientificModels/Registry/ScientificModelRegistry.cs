using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Engine.ScientificModels.Context;
using IQIAIndicator.Engine.ScientificModels.MeanReversion;
using IQIAIndicator.Engine.ScientificModels.Trend;
using IQIAIndicator.Engine.ScientificModels.Validation;

namespace IQIAIndicator.Engine.ScientificModels.Registry;

public sealed class ScientificModelRegistry
{
    private readonly int[]? _momentumLookbacks;

    public ScientificModelRegistry() : this(null)
    {
    }

    /// <summary>Audit 2026-08-30 (P0-2). <paramref name="momentumLookbacks"/> overrides
    /// <c>TimeSeriesMomentumModel</c>'s default lookback set for the trend-following methodology;
    /// null keeps the production default. Threaded from
    /// <c>PipelineParameterOverrides.MomentumLookbacks</c> via <c>SignalEngine</c>.</summary>
    public ScientificModelRegistry(int[]? momentumLookbacks)
    {
        _momentumLookbacks = momentumLookbacks;
    }

    /// <summary>
    /// Sprint 15.5 (C2): explicit, audited per-methodology mapping instead of a single fragile
    /// string-literal special case (Sprint 15.4 audit finding BC-02) that happened to only cover
    /// MeanReversionMethodology by accident, with no indication - to a reader of
    /// MethodologyRegistry's real, non-empty SupportingModels lists for Trending/StructuralBreak - of
    /// why those never produced any scientific evidence.
    ///
    /// Audited directly against Engine/ScientificModels (Sprint 15.4 §C2 / Sprint 15.5 audit): a
    /// methodology is wired here ONLY if every model it would need to run is a genuine, tested
    /// implementation - not a placeholder. BOCPDModel (Engine/ScientificModels/Context/BOCPDModel.cs)
    /// and TimeSeriesMomentumModel (Engine/ScientificModels/Trend/TimeSeriesMomentumModel.cs) are both
    /// literal stubs: Evaluate() unconditionally returns Success=false, Score=0.0, Explanation
    /// ="Scientific model placeholder" - wiring either in would fabricate the appearance of scientific
    /// coverage for a methodology whose defining model produces nothing. No "Random Walk Null Model"
    /// class exists at all. Only MeanReversionMethodology's five constituent models (Kalman Filter,
    /// Ornstein-Uhlenbeck, Dynamic Z-Score, Volatility, SPRT) are genuine, independently tested
    /// implementations - see IQIAIndicator/Tests/ScientificModels/*Tests.cs. This method does not
    /// change, wrap, or invent any model; it only makes explicit which methodologies that already-true
    /// fact covers.
    /// </summary>
    public IReadOnlyList<IScientificModel> Resolve(MethodologySelection methodologySelection)
    {
        ArgumentNullException.ThrowIfNull(methodologySelection);

        return methodologySelection.SelectedMethodology.Name switch
        {
            "MeanReversionMethodology" => new IScientificModel[]
            {
                new KalmanFilterModel(),
                new OrnsteinUhlenbeckModel(),
                new DynamicZScoreModel(),
                new VolatilityModel(),
                new SPRTModel()
            },

            // TrendFollowingMethodology: audit 2026-08-30 (P0-2) wired the now-real
            // TimeSeriesMomentumModel (multi-horizon TSMOM, self-contained - reads only the price
            // history, computes its own volatility scaling and returns autocorrelation). The
            // SupportingModel "BOCPD" (BOCPDModel) is still an unimplemented stub and is NOT wired.
            // VolatilityModel/SPRTModel are hard-gated to MeanReverting (see their own compatible
            // checks) and cannot run here, so this methodology's real coverage is TSMOM alone.
            "TrendFollowingMethodology" => new IScientificModel[]
            {
                new TimeSeriesMomentumModel(_momentumLookbacks)
            },

            // StructuralBreakMethodology: PrimaryModel "BOCPD" (BOCPDModel) is an unimplemented stub.
            "StructuralBreakMethodology" => Array.Empty<IScientificModel>(),

            // RandomWalkMethodology: declares zero SupportingModels, and its PrimaryModel
            // ("Random Walk Null Model") has no corresponding IScientificModel implementation.
            "RandomWalkMethodology" => Array.Empty<IScientificModel>(),

            // StableRangeMethodology / TransitionalMethodology (Sprint 15.5, C3): explicitly
            // unsupported at the MethodologyRegistry level already - no methodology-specific model
            // exists for either regime.
            "StableRangeMethodology" => Array.Empty<IScientificModel>(),
            "TransitionalMethodology" => Array.Empty<IScientificModel>(),

            // UnknownMethodology, or any methodology name not audited above: no coverage.
            _ => Array.Empty<IScientificModel>()
        };
    }
}
