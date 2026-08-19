using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioMappingsTests
{
    [Fact]
    public void CentsToCurrency_ConvertsIntegerCentsToDecimalDollars()
    {
        Assert.Equal(299.00m, MaxioMappings.CentsToCurrency(29900));
        Assert.Equal(29.00m, MaxioMappings.CentsToCurrency(2900));
        Assert.Equal(0.01m, MaxioMappings.CentsToCurrency(1));
    }

    [Fact]
    public void SubscriptionReference_IsStableForUserAndPlan()
    {
        var reference = MaxioMappings.SubscriptionReference("user-123", "eshop-pro");
        Assert.Equal("user-123:eshop-pro", reference);
    }

    [Fact]
    public void ToPlan_MapsProductFields()
    {
        var plan = MaxioMappings.ToPlan(new ProductDto
        {
            Handle = "eshop-pro",
            Name = "Pro Plan",
            Description = "Monthly pro",
            PriceInCents = 29900,
            Interval = 1,
            IntervalUnit = "month"
        });

        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal(1, plan.Interval);
        Assert.Equal("month", plan.IntervalUnit);
    }

    [Fact]
    public void ToCustomerSubscription_PrefersCurrentPeriodEndAsNextBillingDate()
    {
        var nextPeriod = new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);
        var assessment = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
        var mapped = MaxioMappings.ToCustomerSubscription(new SubscriptionDto
        {
            Id = 42,
            State = "active",
            ProductPriceInCents = 29900,
            CurrentPeriodEndsAt = nextPeriod,
            NextAssessmentAt = assessment,
            Product = new ProductDto { Handle = "eshop-pro", Name = "Pro Plan" }
        });

        Assert.Equal(42, mapped.Id);
        Assert.Equal("eshop-pro", mapped.ProductHandle);
        Assert.Equal("Pro Plan", mapped.ProductName);
        Assert.Equal(299.00m, mapped.Price);
        Assert.Equal("active", mapped.State);
        Assert.Equal(nextPeriod, mapped.NextBillingDate);
    }

    [Theory]
    [InlineData("active", true)]
    [InlineData("trialing", true)]
    [InlineData("past_due", true)]
    [InlineData("canceled", false)]
    [InlineData("expired", false)]
    [InlineData("failed_to_create", false)]
    public void IsLive_MatchesBillingStates(string state, bool expected)
    {
        Assert.Equal(expected, MaxioMappings.IsLive(state));
    }

    [Fact]
    public void ResolveProductHandle_DefaultsToEshopProWhenPresent()
    {
        var plans = new List<SubscriptionPlan>
        {
            new() { Handle = "basic-plan", Name = "Basic" },
            new() { Handle = "eshop-pro", Name = "Pro" }
        };

        Assert.Equal("eshop-pro", MaxioMappings.ResolveProductHandle(null, plans));
        Assert.Equal("basic-plan", MaxioMappings.ResolveProductHandle("basic-plan", plans));
    }

    [Fact]
    public void ResolveBaseUrl_UsesOverrideVerbatimThenDerivesFromSubdomain()
    {
        var withOverride = new MaxioOptions { BaseUrl = "https://example.chargify.com" };
        Assert.Equal("https://example.chargify.com/", withOverride.ResolveBaseUrl());

        var derived = new MaxioOptions { Subdomain = "cp-exp-3" };
        Assert.Equal("https://cp-exp-3.chargify.com/", derived.ResolveBaseUrl());
    }
}
