using IQIAIndicator.Tests.ScientificModels;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class SprtModelXunitTests
{
    [Fact]
    public void RunAll() => SPRTModelTests.RunAll();
}
