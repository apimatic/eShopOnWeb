using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class RecurringSubscriptionTests
{
    [Fact]
    public void ConfirmCapturesProviderStateShownToTheShopper()
    {
        var subscription = new RecurringSubscription("user", "plan", "Plan", 2900, "reference");
        var nextBilling = DateTimeOffset.UtcNow.AddMonths(1);

        subscription.MarkSendStarted();
        subscription.Confirm(42, "plan", "Plan", 2900, "USD", "active", nextBilling);

        Assert.True(subscription.SendStarted);
        Assert.Equal(SubscriptionOperationStatus.Confirmed, subscription.OperationStatus);
        Assert.Equal(42, subscription.MaxioSubscriptionId);
        Assert.Equal("active", subscription.ProviderState);
        Assert.Equal(nextBilling, subscription.NextBillingAt);
    }

    [Fact]
    public void AmbiguousWriteRemainsReservedForReconciliation()
    {
        var subscription = new RecurringSubscription("user", "plan", "Plan", 2900, "reference");

        subscription.MarkSendStarted();
        subscription.MarkForReconciliation();

        Assert.True(subscription.SendStarted);
        Assert.Equal(SubscriptionOperationStatus.NeedsReconciliation, subscription.OperationStatus);
    }
}
