using IQIAIndicator.Tests.Fusion;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class FusionStateManagerXunitTests
{
    [Fact]
    public void RunAll() => FusionStateManagerTests.RunAll();
}
