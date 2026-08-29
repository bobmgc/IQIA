using IQIAIndicator.Tests.ScientificModels;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class KalmanFilterModelWindowXunitTests
{
    [Fact]
    public void RunAll() => KalmanFilterModelWindowTests.RunAll();
}
