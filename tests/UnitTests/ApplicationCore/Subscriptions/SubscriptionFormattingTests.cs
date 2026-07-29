using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class SubscriptionFormattingTests
{
    [Fact]
    public void SubscriptionPlan_FormatsMonthlyPrice()
    {
        var plan = new SubscriptionPlan
        {
            Handle = "eshop-pro",
            Name = "Pro Plan",
            PriceInCents = 29900,
            Interval = 1,
            IntervalUnit = "month",
            ProductFamilyHandle = "eshop-subscribe",
        };

        Assert.Equal("$299.00 / month", plan.FormattedPrice);
    }

    [Fact]
    public void SubscriptionPlan_FormatsMultiIntervalPrice()
    {
        var plan = new SubscriptionPlan
        {
            Handle = "quarterly",
            Name = "Quarterly",
            PriceInCents = 9000,
            Interval = 3,
            IntervalUnit = "month",
            ProductFamilyHandle = "eshop-subscribe",
        };

        Assert.Equal("$90.00 / 3 months", plan.FormattedPrice);
    }

    [Fact]
    public void CustomerSubscription_FormatsPrice()
    {
        var subscription = new CustomerSubscription
        {
            PlanHandle = "basic-plan",
            PlanName = "Basic Plan",
            State = "active",
            PriceInCents = 2900,
            Interval = 1,
            IntervalUnit = "month",
            PaymentCollectionMethod = "remittance",
        };

        Assert.Equal("$29.00 / month", subscription.FormattedPrice);
    }
}
