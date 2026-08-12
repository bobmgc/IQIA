using IQIAIndicator.Tests.GoldenDatasets;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class AdfLagSelectionScaleStabilityXunitTests
{
    [Fact]
    public void RunAll() => AdfLagSelectionScaleStabilityTests.RunAll();
}
