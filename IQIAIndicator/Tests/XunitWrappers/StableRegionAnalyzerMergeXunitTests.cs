using IQIAIndicator.Tests.Research.StopLossCalibration;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class StableRegionAnalyzerMergeXunitTests
{
    [Fact]
    public void RunAll() => StableRegionAnalyzerMergeTests.RunAll();
}
