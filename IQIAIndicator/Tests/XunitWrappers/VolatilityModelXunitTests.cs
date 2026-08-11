using IQIAIndicator.Tests.ScientificModels;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class VolatilityModelXunitTests
{
    [Fact]
    public void RunAll() => VolatilityModelTests.RunAll();
}
