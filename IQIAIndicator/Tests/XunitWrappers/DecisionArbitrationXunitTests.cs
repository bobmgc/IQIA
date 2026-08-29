using IQIAIndicator.Tests.Decision;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class DecisionArbitrationXunitTests
{
    [Fact]
    public void RunAll() => DecisionArbitrationTests.RunAll();
}
