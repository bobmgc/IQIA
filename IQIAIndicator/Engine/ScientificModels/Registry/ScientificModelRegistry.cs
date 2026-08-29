using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Engine.ScientificModels.Context;
using IQIAIndicator.Engine.ScientificModels.MeanReversion;
using IQIAIndicator.Engine.ScientificModels.Validation;

namespace IQIAIndicator.Engine.ScientificModels.Registry;

public sealed class ScientificModelRegistry
{
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

            // TrendFollowingMethodology: PrimaryModel "Time Series Momentum" (TimeSeriesMomentumModel)
            // and SupportingModel "BOCPD" (BOCPDModel) are both unimplemented stubs. VolatilityModel
            // alone cannot support this methodology's own defining claim (trend continuation), so
            // returning it in isolation would be partial, misleading coverage rather than real support.
            "TrendFollowingMethodology" => Array.Empty<IScientificModel>(),

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
