using IQIAIndicator.Tests.GoldenDatasets;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class VarianceRatioValidationXunitTests
{
    [Fact]
    public void RunAll() => VarianceRatioValidationTests.RunAll();
}
