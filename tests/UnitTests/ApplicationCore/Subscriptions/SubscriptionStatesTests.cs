using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class SubscriptionStatesTests
{
    [Theory]
    [InlineData(SubscriptionStates.Active)]
    [InlineData(SubscriptionStates.Trialing)]
    [InlineData(SubscriptionStates.Pending)]
    [InlineData(SubscriptionStates.PastDue)]
    [InlineData(SubscriptionStates.OnHold)]
    [InlineData(SubscriptionStates.Suspended)]
    [InlineData(SubscriptionStates.AwaitingSignup)]
    [InlineData("ACTIVE")]
    public void TreatsOngoingStatesAsLive(string state)
    {
        Assert.True(SubscriptionStates.IsLive(state));
        Assert.False(SubscriptionStates.IsTerminal(state));
    }

    [Theory]
    [InlineData(SubscriptionStates.Canceled)]
    [InlineData(SubscriptionStates.Expired)]
    [InlineData(SubscriptionStates.FailedToCreate)]
    [InlineData(SubscriptionStates.TrialEnded)]
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
    public void TreatsAMissingStateAsTerminal(string? state)
    {
        // A subscription with no state cannot be relied on to entitle anyone to anything.
        Assert.True(SubscriptionStates.IsTerminal(state));
    }
}
