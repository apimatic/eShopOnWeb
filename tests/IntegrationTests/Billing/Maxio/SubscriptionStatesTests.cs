#nullable enable
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing.Maxio;

public class SubscriptionStatesTests
{
    [Theory]
    [InlineData("active")]
    [InlineData("trialing")]
    [InlineData("pending")]
    [InlineData("assessing")]
    [InlineData("past_due")]
    [InlineData("on_hold")]
    [InlineData("awaiting_signup")]
    public void TreatsEverythingShortOfEndOfLifeAsLive(string state)
    {
        Assert.True(SubscriptionStates.IsLive(state));
        Assert.False(SubscriptionStates.IsTerminal(state));
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("expired")]
    [InlineData("failed_to_create")]
    [InlineData("trial_ended")]
    [InlineData("CANCELED")]
    public void TreatsEndOfLifeStatesAsTerminal(string state)
    {
        Assert.True(SubscriptionStates.IsTerminal(state));
        Assert.False(SubscriptionStates.IsLive(state));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TreatsAnAbsentStateAsNeitherLiveNorTerminal(string? state)
    {
        Assert.False(SubscriptionStates.IsLive(state));
        Assert.False(SubscriptionStates.IsTerminal(state));
    }
}
