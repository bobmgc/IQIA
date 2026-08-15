using IQIAIndicator.Tests.Research.StopLossCalibration.A1Calibration;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class A1CalibrationSprintXunitTests
{
    [Fact]
    public void RunAll() => A1CalibrationSprintRunner.RunAll();
}
