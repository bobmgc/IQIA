using IQIAIndicator.Tests.TradePlanTests;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class TradePlanBuilderXunitTests
{
    [Fact]
    public void RunAll() => TradePlanBuilderTests.RunAll();
}
