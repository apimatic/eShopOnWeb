using Microsoft.eShopWeb.ApplicationCore.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Billing;

public class SubscriptionPlanTests
{
    [Fact]
    public void ConvertsCentsToMajorUnits()
    {
        var plan = new SubscriptionPlan("eshop-pro", "Pro Plan", null, 29900, 1, "month");

        Assert.Equal(299.00m, plan.Price);
    }

    [Fact]
    public void FormatsPriceWithInterval()
    {
        var plan = new SubscriptionPlan("basic-plan", "Basic Plan", null, 2900, 1, "month");

        Assert.Equal("29.00 / month", plan.FormattedPrice);
    }

    [Fact]
    public void SubscriptionExposesPriceAndAlreadyExistedFlag()
    {
        var subscription = new CustomerSubscription(
            SubscriptionId: 123,
            State: "active",
            PlanHandle: "eshop-pro",
            PlanName: "Pro Plan",
            PriceInCents: 29900,
            Interval: 1,
            IntervalUnit: "month",
            CustomerId: 9,
            CustomerReference: "user-1",
            CurrentPeriodStartedAt: null,
            CurrentPeriodEndsAt: null,
            NextBillingAt: null,
            CreatedAt: null)
        {
            AlreadyExisted = true
        };

        Assert.Equal(299.00m, subscription.Price);
        Assert.True(subscription.AlreadyExisted);
    }
}
