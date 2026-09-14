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
    [InlineData("past_due")]
    [InlineData("unpaid")]
    [InlineData("soft_failure")]
    [InlineData("on_hold")]
    [InlineData("suspended")]
    public void TreatsRecoverableStatesAsLive(string state) => Assert.True(SubscriptionStates.IsLive(state));

    [Theory]
    [InlineData("canceled")]
    [InlineData("expired")]
    [InlineData("failed_to_create")]
    [InlineData("trial_ended")]
    public void TreatsEndOfLifeStatesAsTerminal(string state) => Assert.True(SubscriptionStates.IsTerminal(state));

    [Fact]
    public void IsCaseInsensitive() => Assert.True(SubscriptionStates.IsTerminal("Canceled"));
}
