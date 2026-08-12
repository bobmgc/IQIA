using IQIAIndicator.Tests.Decision;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class MissingEvidenceSemanticsXunitTests
{
    [Fact]
    public void RunAll() => MissingEvidenceSemanticsTests.RunAll();
}
