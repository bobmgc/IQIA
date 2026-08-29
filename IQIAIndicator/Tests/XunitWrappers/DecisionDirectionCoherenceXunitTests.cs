using IQIAIndicator.Tests.EntryTrigger;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class DecisionDirectionCoherenceXunitTests
{
    [Fact]
    public void RunAll() => DecisionDirectionCoherenceTests.RunAll();
}
