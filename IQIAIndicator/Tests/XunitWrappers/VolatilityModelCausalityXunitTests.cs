using IQIAIndicator.Tests.ScientificModels;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class VolatilityModelCausalityXunitTests
{
    [Fact]
    public void RunAll() => VolatilityModelCausalityTests.RunAll();
}
