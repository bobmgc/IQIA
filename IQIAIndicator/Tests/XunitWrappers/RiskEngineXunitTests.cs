using IQIAIndicator.Tests.RiskTests;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class RiskEngineXunitTests
{
    [Fact]
    public void RunAll() => RiskEngineTests.RunAll();
}
