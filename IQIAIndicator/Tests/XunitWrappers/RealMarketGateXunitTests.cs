using IQIAIndicator.Tests.Research.StopLossCalibration.RealMarket;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class RealMarketGateXunitTests
{
    [Fact]
    public void RunAll() => RealMarketGateTests.RunAll();
}
