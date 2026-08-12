using IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class HybridLookAheadAuditXunitTests
{
    [Fact]
    public void RunAll() => VolatilityRegimeLookAheadAuditTests.RunAll();
}
