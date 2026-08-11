using IQIAIndicator.Tests.Decision;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class StructuralBreakRuleXunitTests
{
    [Fact]
    public void RunAll() => StructuralBreakRuleTests.RunAll();
}
