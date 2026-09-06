using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.SubscriptionEndpoints;

public class SubscriptionMappingTests
{
    private static SubscriptionPlan ProPlan() => new()
    {
        Handle = "eshop-pro",
        Name = "Pro Plan",
        PriceInCents = 29900,
        Currency = "USD",
        Interval = BillingInterval.Monthly,
        ProductFamilyHandle = "eshop-subscribe",
    };

    [Fact]
    public void PlanPriceIsExposedInBothMinorAndMajorUnits()
    {
        var dto = ProPlan().ToDto();

        Assert.Equal(29900, dto.PriceInCents);
        Assert.Equal(299.00m, dto.Price);
        Assert.Equal("299.00 USD / month", dto.FormattedPrice);
    }

    [Fact]
    public void FractionalPricesSurviveTheConversion()
    {
        var plan = ProPlan();
        var dto = new SubscriptionPlan
        {
            Handle = plan.Handle,
            Name = plan.Name,
            PriceInCents = 1999,
            Currency = "EUR",
            Interval = new BillingInterval(3, "month"),
        }.ToDto();

        Assert.Equal(19.99m, dto.Price);
        Assert.Equal("19.99 EUR / 3 months", dto.FormattedPrice);
        Assert.Equal(3, dto.BillingIntervalLength);
        Assert.Equal("month", dto.BillingIntervalUnit);
    }

    [Fact]
    public void TrialDetailsAreCarriedThrough()
    {
        var dto = new SubscriptionPlan
        {
            Handle = "trial-plan",
            Name = "Trial Plan",
            PriceInCents = 1000,
            Currency = "USD",
            Interval = BillingInterval.Monthly,
            TrialInterval = 14,
            TrialIntervalUnit = "day",
        }.ToDto();

        Assert.True(dto.HasTrial);
        Assert.Equal(14, dto.TrialIntervalLength);
        Assert.Equal("day", dto.TrialIntervalUnit);
    }

    [Fact]
    public void SubscriptionStateIsPassedThroughVerbatimAndClassified()
    {
        var dto = Subscription("past_due").ToDto();

        Assert.Equal("past_due", dto.State);
        Assert.False(dto.IsActive);

        Assert.True(Subscription("active").ToDto().IsActive);
        Assert.True(Subscription("trialing").ToDto().IsActive);
    }

    [Fact]
    public void SubscriptionCarriesTheDatesAShopperNeedsToSeeAfterSubscribing()
    {
        var nextBilling = new DateTimeOffset(2026, 10, 6, 15, 48, 23, TimeSpan.FromHours(5));
        var subscription = new CustomerSubscription
        {
            Id = "94209904",
            State = "active",
            PlanHandle = "eshop-pro",
            PlanName = "Pro Plan",
            PriceInCents = 29900,
            Currency = "USD",
            Interval = BillingInterval.Monthly,
            CustomerId = "98838455",
            NextBillingAt = nextBilling,
            CurrentPeriodEndsAt = nextBilling,
            Reference = "eshoponweb:subscription:demouser@microsoft.com:eshop-pro",
        };

        var dto = subscription.ToDto();

        Assert.Equal("94209904", dto.Id);
        Assert.Equal("eshop-pro", dto.PlanHandle);
        Assert.Equal("Pro Plan", dto.PlanName);
        Assert.Equal(299.00m, dto.Price);
        Assert.Equal(nextBilling, dto.NextBillingAt);
        Assert.Equal(nextBilling, dto.CurrentPeriodEndsAt);
        Assert.Equal("eshoponweb:subscription:demouser@microsoft.com:eshop-pro", dto.Reference);
        Assert.Equal("98838455", dto.CustomerId);
    }

    [Fact]
    public void ASubscriptionWithoutAPlanStillMaps()
    {
        // Maxio's newer catalog experience allows subscriptions built from components with no product.
        // This integration never creates one, but it must not fail while reading a shared site back.
        var dto = new CustomerSubscription
        {
            Id = "1",
            State = "active",
            PriceInCents = 0,
            Currency = "USD",
            CustomerId = "2",
        }.ToDto();

        Assert.Null(dto.PlanHandle);
        Assert.Null(dto.BillingIntervalLength);
        Assert.Equal("0.00 USD", dto.FormattedPrice);
    }

    private static CustomerSubscription Subscription(string state) => new()
    {
        Id = "1",
        State = state,
        PlanHandle = "eshop-pro",
        PriceInCents = 29900,
        Currency = "USD",
        Interval = BillingInterval.Monthly,
        CustomerId = "2",
    };
}
