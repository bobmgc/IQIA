using IQIAIndicator.Tests.Decision;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class StableRangeRuleXunitTests
{
    [Fact]
    public void RunAll() => StableRangeRuleTests.RunAll();
}
