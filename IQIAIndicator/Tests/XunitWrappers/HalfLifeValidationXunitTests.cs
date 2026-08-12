using IQIAIndicator.Tests.GoldenDatasets;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class HalfLifeValidationXunitTests
{
    [Fact]
    public void RunAll() => HalfLifeValidationTests.RunAll();
}
