using IQIAIndicator.Tests.Research.StopLossCalibration;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class RatioConformanceXunitTests
{
    [Fact]
    public void RunAll() => RatioConformanceTests.RunAll();
}
