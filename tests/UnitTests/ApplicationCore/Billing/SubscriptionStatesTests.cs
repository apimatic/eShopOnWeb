using Microsoft.eShopWeb.ApplicationCore.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Billing;

public class SubscriptionStatesTests
{
    [Theory]
    [InlineData("active")]
    [InlineData("trialing")]
    [InlineData("assessing")]
    [InlineData("pending")]
    [InlineData("paused")]
    [InlineData("awaiting_signup")]
    [InlineData("past_due")]
    [InlineData("soft_failure")]
    [InlineData("unpaid")]
    [InlineData("suspended")]
    [InlineData("on_hold")]
    [InlineData("ACTIVE")]
    public void TreatsStatesWhereTheShopperIsStillEnrolledAsLive(string state)
    {
        Assert.True(SubscriptionStates.IsLive(state));
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("expired")]
    [InlineData("failed_to_create")]
    [InlineData("trial_ended")]
    [InlineData("")]
    [InlineData(null)]
    public void TreatsEndOfLifeStatesAsNotLive(string? state)
    {
        Assert.False(SubscriptionStates.IsLive(state));
    }

    [Fact]
    public void AnUnrecognisedStateIsNotAssumedToBeLive()
    {
        Assert.False(SubscriptionStates.IsLive("some_future_state"));
    }
}
