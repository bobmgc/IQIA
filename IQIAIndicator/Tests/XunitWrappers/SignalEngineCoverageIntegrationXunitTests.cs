using IQIAIndicator.Tests.Signal;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class SignalEngineCoverageIntegrationXunitTests
{
    [Fact]
    public void RunAll() => SignalEngineCoverageIntegrationTests.RunAll();
}
