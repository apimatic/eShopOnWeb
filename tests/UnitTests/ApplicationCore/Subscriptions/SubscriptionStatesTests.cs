using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class SubscriptionStatesTests
{
    [Theory]
    [InlineData("active")]
    [InlineData("trialing")]
    [InlineData("pending")]
    [InlineData("assessing")]
    [InlineData("paused")]
    [InlineData("past_due")]
    [InlineData("soft_failure")]
    [InlineData("unpaid")]
    [InlineData("awaiting_signup")]
    public void TreatsLiveAndProblemStatesAsAnExistingEngagement(string state)
    {
        Assert.True(SubscriptionStates.IsEngaged(state));
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("expired")]
    [InlineData("failed_to_create")]
    [InlineData("on_hold")]
    [InlineData("suspended")]
    [InlineData("trial_ended")]
    public void TreatsEndOfLifeStatesAsNoLongerEngaged(string state)
    {
        Assert.False(SubscriptionStates.IsEngaged(state));
    }

    [Fact]
    public void TreatsAnUnknownOrMissingStateAsNotEngaged()
    {
        Assert.False(SubscriptionStates.IsEngaged(null));
        Assert.False(SubscriptionStates.IsEngaged("something-new"));
    }
}
