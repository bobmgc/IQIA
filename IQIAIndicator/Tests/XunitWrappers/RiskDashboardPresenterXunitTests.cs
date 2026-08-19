using IQIAIndicator.Tests.Visualization;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class RiskDashboardPresenterXunitTests
{
    [Fact]
    public void RunAll() => RiskDashboardPresenterTests.RunAll();
}
