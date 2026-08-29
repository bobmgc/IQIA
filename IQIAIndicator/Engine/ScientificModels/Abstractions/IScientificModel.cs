using IQIAIndicator.Engine.Decision.Core;
using IQIAIndicator.Engine.Methodology.Core;

namespace IQIAIndicator.Engine.ScientificModels.Abstractions;

public interface IScientificModel
{
    string Name { get; }

    string Category { get; }

    ScientificModelResult Evaluate(ScientificModelContext context);
}
