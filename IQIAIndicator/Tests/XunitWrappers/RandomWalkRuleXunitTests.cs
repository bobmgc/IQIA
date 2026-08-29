using IQIAIndicator.Tests.Decision;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class RandomWalkRuleXunitTests
{
    [Fact]
    public void RunAll() => RandomWalkRuleTests.RunAll();
}
