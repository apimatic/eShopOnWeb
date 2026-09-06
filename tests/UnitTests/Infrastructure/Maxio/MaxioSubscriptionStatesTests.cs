using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSubscriptionStatesTests
{
    [Theory]
    [InlineData(MaxioSubscriptionStates.Active)]
    [InlineData(MaxioSubscriptionStates.Trialing)]
    [InlineData(MaxioSubscriptionStates.Pending)]
    [InlineData(MaxioSubscriptionStates.Assessing)]
    [InlineData(MaxioSubscriptionStates.Paused)]
    [InlineData(MaxioSubscriptionStates.PastDue)]
    [InlineData(MaxioSubscriptionStates.SoftFailure)]
    [InlineData(MaxioSubscriptionStates.Unpaid)]
    [InlineData(MaxioSubscriptionStates.OnHold)]
    [InlineData(MaxioSubscriptionStates.Suspended)]
    public void OngoingStatesCountAsLive(string state) => Assert.True(MaxioSubscriptionStates.IsLive(state));

    [Theory]
    [InlineData(MaxioSubscriptionStates.Canceled)]
    [InlineData(MaxioSubscriptionStates.Expired)]
    [InlineData(MaxioSubscriptionStates.FailedToCreate)]
    [InlineData(MaxioSubscriptionStates.TrialEnded)]
    public void EndOfLifeStatesAreNotLive(string state) => Assert.False(MaxioSubscriptionStates.IsLive(state));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UnknownStateIsNotLive(string? state) => Assert.False(MaxioSubscriptionStates.IsLive(state));
}
