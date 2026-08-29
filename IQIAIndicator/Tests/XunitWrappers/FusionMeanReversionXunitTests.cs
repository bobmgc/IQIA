using IQIAIndicator.Tests.Fusion;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class FusionMeanReversionXunitTests
{
    [Fact]
    public void RunAll() => MeanReversionRuleTests.RunAll();
}
