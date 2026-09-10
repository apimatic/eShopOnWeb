using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Subscriptions;

public class SubscriptionDomainTests
{
    [Fact]
    public void PlanPriceDisplay_FormatsMonthlyPrice()
    {
        var plan = new SubscriptionPlan("eshop-pro", "Pro Plan", null, 29900, 1, "month", false);

        Assert.Equal("$299.00/month", plan.PriceDisplay);
    }

    [Fact]
    public void PlanPriceDisplay_PluralizesMultiIntervalUnits()
    {
        var plan = new SubscriptionPlan("q", "Quarterly", null, 9000, 3, "month", false);

        Assert.Equal("$90.00/3 months", plan.PriceDisplay);
    }

    [Fact]
    public void SummaryPriceDisplay_FormatsFromCents()
    {
        var summary = new SubscriptionSummary(
            id: 1, state: "active", planHandle: "basic-plan", planName: "Basic Plan",
            priceInCents: 2900, interval: 1, intervalUnit: "month",
            paymentCollectionMethod: "remittance",
            currentPeriodStartedAt: DateTimeOffset.UtcNow, nextBillingAt: DateTimeOffset.UtcNow);

        Assert.Equal("$29.00/month", summary.PriceDisplay);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SubscriberIdentity_RejectsMissingEmail(string? email)
    {
        Assert.ThrowsAny<ArgumentException>(() => new SubscriberIdentity("user-1", email!));
    }

    [Fact]
    public void SubscriberIdentity_KeepsProvidedValues()
    {
        var subscriber = new SubscriberIdentity("user-1", "demo@example.com");

        Assert.Equal("user-1", subscriber.UserId);
        Assert.Equal("demo@example.com", subscriber.Email);
    }
}
