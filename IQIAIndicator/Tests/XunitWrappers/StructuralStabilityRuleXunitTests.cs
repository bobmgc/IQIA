using IQIAIndicator.Tests.Fusion;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class StructuralStabilityRuleXunitTests
{
    [Fact]
    public void RunAll() => StructuralStabilityRuleTests.RunAll();
}
