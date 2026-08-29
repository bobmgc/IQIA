using IQIAIndicator.Tests.Research.StopLossCalibration.Hybrid;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class HybridSprintXunitTests
{
    [Fact]
    public void RunAll() => HybridSprintRunner.RunAll();
}
