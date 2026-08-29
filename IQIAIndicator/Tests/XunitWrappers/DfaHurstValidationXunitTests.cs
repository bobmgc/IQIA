using IQIAIndicator.Tests.GoldenDatasets;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class DfaHurstValidationXunitTests
{
    [Fact]
    public void RunAll() => DfaHurstValidationTests.RunAll();
}
