using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private readonly IMaxioAdvancedBillingClient _maxio = Substitute.For<IMaxioAdvancedBillingClient>();
    private readonly MaxioOptions _options = new()
    {
        ApiKey = "test-key",
        Subdomain = "example",
        ProductFamilyHandle = "eshop-subscribe"
    };

    private MaxioSubscriptionBillingService CreateSut() =>
        new(_maxio, Options.Create(_options), NullLogger<MaxioSubscriptionBillingService>.Instance);

    [Fact]
    public void CentsToAmount_DividesByOneHundred()
    {
        Assert.Equal(299.00m, MaxioSubscriptionBillingService.CentsToAmount(29900));
        Assert.Equal(29.00m, MaxioSubscriptionBillingService.CentsToAmount(2900));
    }

    [Fact]
    public void SplitName_UsesEmailLocalPart()
    {
        var (first, last) = MaxioSubscriptionBillingService.SplitName("demouser@microsoft.com", null);
        Assert.Equal("Demouser", first);
        Assert.Equal("Subscriber", last);
    }

    [Fact]
    public void ResolveBaseAddress_UsesConfiguredBaseUrlVerbatimWithTrailingSlash()
    {
        var address = MaxioAdvancedBillingClient.ResolveBaseAddress(new MaxioOptions
        {
            BaseUrl = "https://billing.example.test"
        });

        Assert.Equal("https://billing.example.test/", address);
    }

    [Fact]
    public void ResolveBaseAddress_DerivesChargifyHostFromSubdomain()
    {
        var address = MaxioAdvancedBillingClient.ResolveBaseAddress(new MaxioOptions
        {
            Subdomain = "cp-exp-3"
        });

        Assert.Equal("https://cp-exp-3.chargify.com/", address);
    }

    [Fact]
    public async Task ListPlansAsync_MapsMaxioProducts()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                new()
                {
                    Handle = "eshop-pro",
                    Name = "Pro Plan",
                    Description = "Pro",
                    PriceInCents = 29900,
                    Interval = 1,
                    IntervalUnit = "month"
                }
            });

        var plans = await CreateSut().ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(299.00m, plan.Price);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingLiveSubscriptionWithoutCreatingAnother()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct> { new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" } });
        _maxio.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = "user-1" });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                new()
                {
                    Id = 99,
                    State = "active",
                    ProductPriceInCents = 29900,
                    Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan" },
                    NextAssessmentAt = DateTimeOffset.Parse("2026-09-20T00:00:00Z")
                }
            });

        var result = await CreateSut().SubscribeAsync(
            new ShopperIdentity("user-1", "demouser@microsoft.com", "demouser@microsoft.com"),
            "eshop-pro");

        Assert.Equal(99, result.Id);
        Assert.Equal("active", result.State);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsWhenPlanIsNotInFamily()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct> { new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" } });

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() =>
            CreateSut().SubscribeAsync(
                new ShopperIdentity("user-1", "a@b.com", "a@b.com"),
                "unknown-plan"));
    }
}
