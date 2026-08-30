using IQIAIndicator.Tests.ScientificModels;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class TimeSeriesMomentumModelXunitTests
{
    [Fact]
    public void RunAll() => TimeSeriesMomentumModelTests.RunAll();
}
