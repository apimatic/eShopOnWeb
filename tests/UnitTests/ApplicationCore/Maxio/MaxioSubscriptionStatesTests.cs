using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Maxio;

public class MaxioSubscriptionStatesTests
{
    [Theory]
    [InlineData("active")]
    [InlineData("trialing")]
    [InlineData("past_due")]
    [InlineData("unpaid")]
    [InlineData("paused")]
    [InlineData("assessing")]
    [InlineData("pending")]
    [InlineData("on_hold")]
    [InlineData("ACTIVE")]
    public void LiveStatesAreConsideredLive(string state)
    {
        Assert.True(MaxioSubscriptionStates.IsLive(state));
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("expired")]
    [InlineData("failed_to_create")]
    [InlineData("trial_ended")]
    [InlineData("CANCELED")]
    public void DeadStatesAreNotConsideredLive(string state)
    {
        Assert.False(MaxioSubscriptionStates.IsLive(state));
    }
}
