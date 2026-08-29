using IQIAIndicator.Tests.GoldenDatasets;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class CusumValidationXunitTests
{
    [Fact]
    public void RunAll() => CusumValidationTests.RunAll();
}
