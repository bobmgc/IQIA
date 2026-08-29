using IQIAIndicator.Tests.Fusion;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class PersistenceRuleXunitTests
{
    [Fact]
    public void RunAll() => PersistenceRuleTests.RunAll();
}
