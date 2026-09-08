using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.SubscriptionBilling;

public class SubscriptionStatusTests
{
    [Theory]
    [InlineData("canceled")]
    [InlineData("expired")]
    [InlineData("trial_ended")]
    [InlineData("failed_to_create")]
    public void IsEndedStateReturnsTrueForEndOfLifeStates(string state)
    {
        Assert.True(SubscriptionStatus.IsEndedState(state));
    }

    [Theory]
    [InlineData("active")]
    [InlineData("trialing")]
    [InlineData("past_due")]
    [InlineData("unpaid")]
    [InlineData("on_hold")]
    [InlineData("suspended")]
    [InlineData("pending")]
    [InlineData("assessing")]
    public void IsEndedStateReturnsFalseForLiveAndProblemStates(string state)
    {
        Assert.False(SubscriptionStatus.IsEndedState(state));
    }
}
