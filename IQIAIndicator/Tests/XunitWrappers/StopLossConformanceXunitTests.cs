using IQIAIndicator.Tests.Research.StopLossCalibration;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class StopLossConformanceXunitTests
{
    [Fact]
    public void RunAll() => ConformanceTests.RunAll();
}
