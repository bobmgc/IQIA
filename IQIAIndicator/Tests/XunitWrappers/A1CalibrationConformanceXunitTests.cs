using IQIAIndicator.Tests.Research.StopLossCalibration.A1Calibration;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class A1CalibrationConformanceXunitTests
{
    [Fact]
    public void RunAll() => A1CalibrationConformanceTests.RunAll();
}
