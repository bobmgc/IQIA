using IQIAIndicator.Tests.Research.StopLossCalibration;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class StopLossCalibrationPocXunitTests
{
    [Fact]
    public void RunAll() => StopLossCalibrationPocTests.RunAll();
}
