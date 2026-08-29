using IQIAIndicator.Tests.Infrastructure.ATAS;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class ATASRuntimeDiagnosticsXunitTests
{
    [Fact]
    public void RunAll() => ATASRuntimeDiagnosticsTests.RunAll();
}
