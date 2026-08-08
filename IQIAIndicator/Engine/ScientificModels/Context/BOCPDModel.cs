using IQIAIndicator.Engine.ScientificModels.Abstractions;

namespace IQIAIndicator.Engine.ScientificModels.Context;

public sealed class BOCPDModel : IScientificModel
{
    public string Name => "BOCPDModel";

    public string Category => "Context";

    public ScientificModelResult Evaluate(ScientificModelContext context)
    {
        return new ScientificModelResult(
            Name,
            false,
            0.0,
            "Scientific model placeholder");
    }
}
