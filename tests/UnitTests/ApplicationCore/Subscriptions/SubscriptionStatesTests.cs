using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

/// <summary>
/// Whether a state counts as "live" decides whether subscribing again is a duplicate, so the
/// classification is deliberately conservative: only states Maxio documents as end-of-life free the
/// shopper to subscribe again.
/// </summary>
public class SubscriptionStatesTests
{
    [Theory]
    [InlineData("active")]
    [InlineData("trialing")]
    [InlineData("pending")]
    [InlineData("assessing")]
    [InlineData("past_due")]
    [InlineData("soft_failure")]
    [InlineData("unpaid")]
    [InlineData("on_hold")]
    [InlineData("suspended")]
    [InlineData("paused")]
    [InlineData("awaiting_signup")]
    public void StatesTheShopperCanReturnFromCountAsLive(string state)
    {
        Assert.True(SubscriptionStates.IsLive(state));
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("expired")]
    [InlineData("failed_to_create")]
    [InlineData("trial_ended")]
    public void EndOfLifeStatesFreeTheShopperToSubscribeAgain(string state)
    {
        Assert.False(SubscriptionStates.IsLive(state));
    }

    [Fact]
    public void AStateMaxioAddsLaterIsTreatedAsLive()
    {
        // Guessing "ended" for an unknown state would let a shopper be billed twice; guessing "live"
        // only makes them ask support to re-subscribe.
        Assert.True(SubscriptionStates.IsLive("some_future_state"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AbsentStateIsNeitherLiveNorHealthy(string? state)
    {
        Assert.False(SubscriptionStates.IsLive(state));
        Assert.False(SubscriptionStates.IsHealthy(state));
    }

    [Theory]
    [InlineData("active", true)]
    [InlineData("trialing", true)]
    [InlineData("past_due", false)]
    [InlineData("unpaid", false)]
    [InlineData("canceled", false)]
    public void OnlyUnproblematicStatesAreHealthy(string state, bool expected)
    {
        Assert.Equal(expected, SubscriptionStates.IsHealthy(state));
    }

    [Fact]
    public void ClassificationIgnoresCasing()
    {
        Assert.False(SubscriptionStates.IsLive("CANCELED"));
        Assert.True(SubscriptionStates.IsHealthy("Active"));
    }
}
