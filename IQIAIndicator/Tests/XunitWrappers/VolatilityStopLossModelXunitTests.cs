using IQIAIndicator.Tests.RiskTests;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class VolatilityStopLossModelXunitTests
{
    [Fact]
    public void RunAll() => VolatilityStopLossModelTests.RunAll();
}
