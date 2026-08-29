using IQIAIndicator.Tests.GoldenDatasets;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class BaiPerronValidationXunitTests
{
    [Fact]
    public void RunAll() => BaiPerronValidationTests.RunAll();
}
