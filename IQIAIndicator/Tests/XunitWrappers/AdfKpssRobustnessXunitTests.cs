using IQIAIndicator.Tests.GoldenDatasets;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class AdfKpssRobustnessXunitTests
{
    [Fact]
    public void RunAll() => AdfKpssRobustnessTests.RunAll();
}
