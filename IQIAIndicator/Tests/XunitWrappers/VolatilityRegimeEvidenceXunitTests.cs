using IQIAIndicator.Tests.GoldenDatasets;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class VolatilityRegimeEvidenceXunitTests
{
    [Fact]
    public void RunAll() => VolatilityRegimeEvidenceTests.RunAll();
}
