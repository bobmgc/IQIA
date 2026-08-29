using IQIAIndicator.Tests.Fusion;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class FusionRandomWalkXunitTests
{
    [Fact]
    public void RunAll() => RandomWalkRuleTests.RunAll();
}
