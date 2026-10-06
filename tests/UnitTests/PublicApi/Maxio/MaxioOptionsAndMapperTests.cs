using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Maxio;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveBaseUrl_DerivesFromSubdomainWhenNoOverride()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-4", ApiKey = "key" };

        Assert.Equal("https://cp-exp-4.chargify.com", options.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_UsesOverrideVerbatimWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "cp-exp-4",
            ApiKey = "key",
            BaseUrl = "https://custom.example.com/api"
        };

        Assert.Equal("https://custom.example.com/api", options.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_TrimsTrailingSlashFromOverride()
    {
        var options = new MaxioOptions
        {
            Subdomain = "cp-exp-4",
            ApiKey = "key",
            BaseUrl = "https://custom.example.com/api/"
        };

        Assert.Equal("https://custom.example.com/api", options.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_ThrowsWhenSubdomainMissingAndNoOverride()
    {
        var options = new MaxioOptions { ApiKey = "key" };

        Assert.Throws<InvalidOperationException>(() => options.ResolveBaseUrl());
    }
}

public class SubscriptionDtoMapperTests
{
    [Fact]
    public void FormatPrice_FormatsCentsAsUsd()
    {
        Assert.Equal("$299.00", SubscriptionDtoMapper.FormatPrice(29900));
        Assert.Equal("$29.00", SubscriptionDtoMapper.FormatPrice(2900));
        Assert.Equal("$0.01", SubscriptionDtoMapper.FormatPrice(1));
    }

    [Fact]
    public void ToPlanDto_MapsProductFields()
    {
        var product = new MaxioProduct
        {
            Id = 1,
            Name = "Pro Plan",
            Handle = "eshop-pro",
            Description = "A plan",
            PriceInCents = 29900,
            Interval = 1,
            IntervalUnit = "month",
            ProductFamily = new MaxioProductFamily { Handle = "eshop-subscribe" }
        };

        var dto = SubscriptionDtoMapper.ToPlanDto(product);

        Assert.Equal("eshop-pro", dto.Handle);
        Assert.Equal("Pro Plan", dto.Name);
        Assert.Equal("A plan", dto.Description);
        Assert.Equal(29900, dto.PriceInCents);
        Assert.Equal("$299.00", dto.Price);
        Assert.Equal(1, dto.Interval);
        Assert.Equal("month", dto.IntervalUnit);
        Assert.Equal("eshop-subscribe", dto.ProductFamilyHandle);
    }

    [Fact]
    public void ToSubscriptionDto_MapsSubscriptionFields()
    {
        var subscription = new MaxioSubscription
        {
            Id = 42,
            State = "active",
            ProductPriceInCents = 29900,
            CurrentPeriodEndsAt = new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero),
            Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan", Interval = 1, IntervalUnit = "month" },
            Customer = new MaxioCustomer { Id = 7 }
        };

        var dto = SubscriptionDtoMapper.ToSubscriptionDto(subscription);

        Assert.Equal(42, dto.Id);
        Assert.Equal("active", dto.State);
        Assert.Equal("eshop-pro", dto.PlanHandle);
        Assert.Equal("Pro Plan", dto.PlanName);
        Assert.Equal("$299.00", dto.Price);
        Assert.Equal(7, dto.CustomerId);
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero), dto.CurrentPeriodEndsAt);
    }
}
