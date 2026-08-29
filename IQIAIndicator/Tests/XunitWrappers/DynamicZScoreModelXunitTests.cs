using IQIAIndicator.Tests.ScientificModels;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class DynamicZScoreModelXunitTests
{
    [Fact]
    public void RunAll() => DynamicZScoreModelTests.RunAll();
}
