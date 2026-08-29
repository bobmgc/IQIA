using IQIAIndicator.Tests.ScientificModels;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class OrnsteinUhlenbeckModelXunitTests
{
    [Fact]
    public void RunAll() => OrnsteinUhlenbeckModelTests.RunAll();
}
