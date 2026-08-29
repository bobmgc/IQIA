using IQIAIndicator.Tests.Fusion;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class StationarityRuleXunitTests
{
    [Fact]
    public void RunAll() => StationarityRuleTests.RunAll();
}
