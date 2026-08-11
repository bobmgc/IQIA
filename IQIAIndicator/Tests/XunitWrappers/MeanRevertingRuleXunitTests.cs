using IQIAIndicator.Tests.Decision;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class MeanRevertingRuleXunitTests
{
    [Fact]
    public void RunAll() => MeanRevertingRuleTests.RunAll();
}
