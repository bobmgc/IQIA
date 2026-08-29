using IQIAIndicator.Tests.Methodology;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class MethodologyRegistryCoverageXunitTests
{
    [Fact]
    public void RunAll() => MethodologyRegistryCoverageTests.RunAll();
}
