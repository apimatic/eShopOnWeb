using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class SubscriptionStatesTests
{
    [Theory]
    [InlineData(SubscriptionStates.Active)]
    [InlineData(SubscriptionStates.Trialing)]
    [InlineData(SubscriptionStates.Pending)]
    [InlineData(SubscriptionStates.AwaitingSignup)]
    // Dunning and hold states still represent an existing enrolment, so re-subscribing would duplicate it.
    [InlineData(SubscriptionStates.PastDue)]
    [InlineData(SubscriptionStates.Unpaid)]
    [InlineData(SubscriptionStates.OnHold)]
    [InlineData(SubscriptionStates.Suspended)]
    public void LiveStatesBlockADuplicateSignup(string state) => Assert.True(SubscriptionStates.IsLive(state));

    [Theory]
    [InlineData(SubscriptionStates.Canceled)]
    [InlineData(SubscriptionStates.Expired)]
    [InlineData(SubscriptionStates.TrialEnded)]
    [InlineData(SubscriptionStates.FailedToCreate)]
    public void TerminalStatesAllowASignupAgain(string state) => Assert.False(SubscriptionStates.IsLive(state));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnAbsentStateIsNotTreatedAsLive(string? state) => Assert.False(SubscriptionStates.IsLive(state));
}
