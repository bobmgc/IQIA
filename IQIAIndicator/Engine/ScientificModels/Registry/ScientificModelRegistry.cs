using IQIAIndicator.Engine.Methodology.Core;
using IQIAIndicator.Engine.ScientificModels.Abstractions;
using IQIAIndicator.Engine.ScientificModels.Context;
using IQIAIndicator.Engine.ScientificModels.MeanReversion;
using IQIAIndicator.Engine.ScientificModels.Validation;

namespace IQIAIndicator.Engine.ScientificModels.Registry;

public sealed class ScientificModelRegistry
{
    public IReadOnlyList<IScientificModel> Resolve(MethodologySelection methodologySelection)
    {
        ArgumentNullException.ThrowIfNull(methodologySelection);

        if (methodologySelection.SelectedMethodology.Name == "MeanReversionMethodology")
        {
            return new IScientificModel[]
            {
                new KalmanFilterModel(),
                new OrnsteinUhlenbeckModel(),
                new DynamicZScoreModel(),
                new VolatilityModel(),
                new SPRTModel()
            };
        }

        return Array.Empty<IScientificModel>();
    }
}
