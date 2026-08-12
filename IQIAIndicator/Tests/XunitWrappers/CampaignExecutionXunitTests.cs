using IQIAIndicator.Tests.Research.StopLossCalibration;
using Xunit;

namespace IQIAIndicator.Tests.XunitWrappers;

public sealed class CampaignExecutionXunitTests
{
    [Fact]
    public void RunAll() => CampaignExecutionTests.RunAll();
}
