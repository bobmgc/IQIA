using IQIAIndicator.Tests.GoldenDatasets;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class AdfKpssValidationXunitTests
{
    [Fact]
    public void RunAll() => AdfKpssValidationTests.RunAll();
}
