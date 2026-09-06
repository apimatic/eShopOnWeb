using AdvancedBilling.Standard.Models;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioEnumsTests
{
    [Theory]
    [InlineData(SubscriptionState.Active, "active")]
    [InlineData(SubscriptionState.PastDue, "past_due")]
    [InlineData(SubscriptionState.TrialEnded, "trial_ended")]
    [InlineData(SubscriptionState.FailedToCreate, "failed_to_create")]
    [InlineData(SubscriptionState.AwaitingSignup, "awaiting_signup")]
    public void ReportsTheStateStringAdvancedBillingUsesOnTheWire(SubscriptionState state, string expected)
    {
        Assert.Equal(expected, MaxioEnums.ToWireValue(state));
    }

    [Theory]
    [InlineData(IntervalUnit.Month, "month")]
    [InlineData(IntervalUnit.Day, "day")]
    public void ReportsTheIntervalUnitAdvancedBillingUsesOnTheWire(IntervalUnit unit, string expected)
    {
        Assert.Equal(expected, MaxioEnums.ToWireValue(unit));
    }

    [Theory]
    [InlineData(CollectionMethod.Remittance, "remittance")]
    [InlineData(CollectionMethod.Automatic, "automatic")]
    public void ReportsTheCollectionMethodAdvancedBillingUsesOnTheWire(CollectionMethod method, string expected)
    {
        Assert.Equal(expected, MaxioEnums.ToWireValue(method));
    }

    [Fact]
    public void ReportsNullForAMissingValue()
    {
        Assert.Null(MaxioEnums.ToWireValueOrNull<SubscriptionState>(null));
    }

    [Theory]
    [InlineData(SubscriptionState.Active)]
    [InlineData(SubscriptionState.Trialing)]
    [InlineData(SubscriptionState.Pending)]
    [InlineData(SubscriptionState.Assessing)]
    [InlineData(SubscriptionState.AwaitingSignup)]
    [InlineData(SubscriptionState.PastDue)]
    [InlineData(SubscriptionState.SoftFailure)]
    [InlineData(SubscriptionState.Unpaid)]
    [InlineData(SubscriptionState.Paused)]
    public void TreatsStatesASubscriptionCanRecoverFromAsLive(SubscriptionState state)
    {
        Assert.True(MaxioEnums.IsLive(state));
    }

    [Theory]
    [InlineData(SubscriptionState.OnHold)]
    [InlineData(SubscriptionState.Suspended)]
    public void TreatsTemporarilyStoppedSubscriptionsAsLive(SubscriptionState state)
    {
        // Advanced Billing files these under "End of Life", but both are expected to resume. Enrolling a
        // shopper again while one is outstanding would have them paying twice once it does.
        Assert.True(MaxioEnums.IsLive(state));
    }

    [Theory]
    [InlineData(SubscriptionState.Canceled)]
    [InlineData(SubscriptionState.Expired)]
    [InlineData(SubscriptionState.FailedToCreate)]
    [InlineData(SubscriptionState.TrialEnded)]
    public void TreatsTerminalStatesAsNotLiveSoTheShopperCanSubscribeAgain(SubscriptionState state)
    {
        Assert.False(MaxioEnums.IsLive(state));
    }

    [Fact]
    public void TreatsAMissingStateAsNotLive()
    {
        Assert.False(MaxioEnums.IsLive(null));
    }
}
