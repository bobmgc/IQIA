using IQIAIndicator.Tests.RiskTests;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class RiskEngineIntegrationXunitTests
{
    [Fact]
    public void RunAll() => RiskEngineIntegrationTests.RunAll();
}
