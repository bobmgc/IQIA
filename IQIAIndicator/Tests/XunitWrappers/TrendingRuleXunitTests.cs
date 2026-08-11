using IQIAIndicator.Tests.Decision;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class TrendingRuleXunitTests
{
    [Fact]
    public void RunAll() => TrendingRuleTests.RunAll();
}
