using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingTests;

public class MaxioSubscriptionBillingServiceTests
{
    private readonly IMaxioAdvancedBillingClient _maxio = Substitute.For<IMaxioAdvancedBillingClient>();
    private readonly IAppLogger<MaxioSubscriptionBillingService> _logger = Substitute.For<IAppLogger<MaxioSubscriptionBillingService>>();
    private readonly ShopperBillingProfile _shopper = new("user-1", "demouser@microsoft.com", "demouser@microsoft.com");

    [Fact]
    public async Task ListPlans_ReturnsActiveProductsInConfiguredFamily()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>()).Returns(new List<MaxioProduct>
        {
            new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" },
            new() { Handle = "archived", Name = "Old", PriceInCents = 100, Interval = 1, IntervalUnit = "month", ArchivedAt = DateTimeOffset.UtcNow }
        });

        var result = await CreateSut().ListPlansAsync();

        Assert.Single(result);
        Assert.Equal("eshop-pro", result[0].Handle);
        Assert.Equal(299.00m, result[0].Price);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription()
    {
        SetupCatalog();
        _maxio.FindCustomerByReferenceAsync("eshop:user-1", Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        _maxio.CreateCustomerAsync(Arg.Any<CreateMaxioCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = "eshop:user-1" });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>());
        _maxio.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription
            {
                Id = 99,
                State = "active",
                ProductPriceInCents = 29900,
                NextAssessmentAt = DateTimeOffset.Parse("2026-09-19T00:00:00Z"),
                Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan" }
            });

        var result = await CreateSut().SubscribeAsync(_shopper, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal("eshop-pro", result.Subscription.ProductHandle);
        Assert.Equal(299.00m, result.Subscription.Price);
        await _maxio.Received(1).CreateCustomerAsync(
            Arg.Is<CreateMaxioCustomer>(c => c.Reference == "eshop:user-1" && c.Email == _shopper.Email),
            Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateMaxioSubscription>(s => s.CustomerId == 42 && s.ProductHandle == "eshop-pro" && s.Reference == "eshop:user-1:eshop-pro"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_IsIdempotentWhenSubscriptionAlreadyExists()
    {
        SetupCatalog();
        _maxio.FindCustomerByReferenceAsync("eshop:user-1", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = "eshop:user-1" });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>
        {
            new()
            {
                Id = 99,
                State = "active",
                ProductPriceInCents = 29900,
                Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan" }
            }
        });

        var result = await CreateSut().SubscribeAsync(_shopper, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateMaxioCustomer>(), Arg.Any<CancellationToken>());
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_ReturnsExistingWhenCreateCollides()
    {
        SetupCatalog();
        _maxio.FindCustomerByReferenceAsync("eshop:user-1", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = "eshop:user-1" });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>(), new List<MaxioSubscription>
            {
                new()
                {
                    Id = 77,
                    State = "active",
                    ProductPriceInCents = 2900,
                    Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan" }
                }
            });
        _maxio.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscription>(), Arg.Any<CancellationToken>())
            .Returns<MaxioSubscription>(_ => throw new MaxioBillingException(422, "reference has already been taken"));

        var result = await CreateSut().SubscribeAsync(_shopper, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(77, result.Subscription.Id);
    }

    [Fact]
    public async Task Subscribe_ThrowsWhenPlanIsUnknown()
    {
        SetupCatalog();

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateSut().SubscribeAsync(_shopper, "not-a-plan"));
    }

    [Fact]
    public async Task GetMySubscriptions_ReturnsEmptyWhenCustomerDoesNotExist()
    {
        _maxio.FindCustomerByReferenceAsync("eshop:user-1", Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);

        var result = await CreateSut().GetMySubscriptionsAsync("user-1");

        Assert.Empty(result);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetMySubscriptions_MapsMaxioSubscriptions()
    {
        _maxio.FindCustomerByReferenceAsync("eshop:user-1", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42 });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>
        {
            new()
            {
                Id = 99,
                State = "active",
                ProductPriceInCents = 29900,
                NextAssessmentAt = DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
                Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan" }
            }
        });

        var result = await CreateSut().GetMySubscriptionsAsync("user-1");

        var subscription = Assert.Single(result);
        Assert.Equal("Pro Plan", subscription.ProductName);
        Assert.Equal("active", subscription.State);
        Assert.Equal(299.00m, subscription.Price);
    }

    [Fact]
    public void ResolveBaseUrl_UsesOverrideWhenSet()
    {
        var options = new MaxioOptions { Subdomain = "ignored", BaseUrl = "https://example.test/ab" };
        Assert.Equal("https://example.test/ab/", options.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_DerivesChargifyHostFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-4" };
        Assert.Equal("https://cp-exp-4.chargify.com/", options.ResolveBaseUrl());
    }

    private void SetupCatalog()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>()).Returns(new List<MaxioProduct>
        {
            new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" },
            new() { Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" }
        });
    }

    private MaxioSubscriptionBillingService CreateSut()
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "cp-exp-4",
            ProductFamilyHandle = "eshop-subscribe"
        });
        return new MaxioSubscriptionBillingService(_maxio, options, _logger);
    }
}
