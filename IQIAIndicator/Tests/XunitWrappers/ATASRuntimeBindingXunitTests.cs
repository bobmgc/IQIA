using IQIAIndicator.Tests.Infrastructure.ATAS;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class ATASRuntimeBindingXunitTests
{
    [Fact]
    public void RunAll() => ATASRuntimeBindingTests.RunAll();
}
