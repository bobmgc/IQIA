using IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class HybridConformanceXunitTests
{
    [Fact]
    public void RunAll() => HybridConformanceTests.RunAll();
}
