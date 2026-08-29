using IQIAIndicator.Tests.Signal;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class DirectionEndToEndXunitTests
{
    [Fact]
    public void RunAll() => DirectionEndToEndTests.RunAll();
}
