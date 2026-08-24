using IQIAIndicator.Tests.Fusion;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class PersistenceZeroVarianceSyntheticXunitTests
{
    [Fact]
    public void RunAll() => PersistenceZeroVarianceSyntheticTests.RunAll();
}
