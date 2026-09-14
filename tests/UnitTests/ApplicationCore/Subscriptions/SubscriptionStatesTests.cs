using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class SubscriptionStatesTests
{
    [Theory]
    [InlineData("active")]
    [InlineData("trialing")]
    [InlineData("past_due")]
    [InlineData("on_hold")]
    [InlineData("awaiting_signup")]
    public void A_live_subscription_still_occupies_its_plan(string state) =>
        Assert.True(SubscriptionStates.IsLive(state));

    [Theory]
    [InlineData("canceled")]
    [InlineData("expired")]
    [InlineData("failed_to_create")]
    [InlineData("trial_ended")]
    public void An_ended_subscription_leaves_the_plan_free_to_take_again(string state) =>
        Assert.False(SubscriptionStates.IsLive(state));

    [Theory]
    [InlineData("active")]
    [InlineData("trialing")]
    [InlineData("past_due")]
    public void Access_is_granted_while_the_shopper_is_being_served(string state) =>
        Assert.True(SubscriptionStates.GrantsEntitlement(state));

    [Theory]
    [InlineData("canceled")]
    [InlineData("unpaid")]
    [InlineData("on_hold")]
    [InlineData("suspended")]
    public void Access_is_withheld_once_the_shopper_stops_being_served(string state) =>
        Assert.False(SubscriptionStates.GrantsEntitlement(state));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("some_state_added_upstream_later")]
    public void An_unrecognised_state_never_silently_grants_access(string? state)
    {
        Assert.False(SubscriptionStates.IsLive(state));
        Assert.False(SubscriptionStates.GrantsEntitlement(state));
    }

    [Fact]
    public void State_matching_is_case_insensitive()
    {
        Assert.True(SubscriptionStates.IsLive("ACTIVE"));
        Assert.True(SubscriptionStates.GrantsEntitlement("Active"));
    }
}
