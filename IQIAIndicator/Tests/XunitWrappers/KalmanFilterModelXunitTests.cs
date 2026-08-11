using IQIAIndicator.Tests.ScientificModels;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class KalmanFilterModelXunitTests
{
    [Fact]
    public void RunAll() => KalmanFilterModelTests.RunAll();
}
