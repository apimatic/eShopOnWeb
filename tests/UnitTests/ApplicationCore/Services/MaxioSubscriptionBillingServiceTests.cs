using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class MaxioSubscriptionBillingServiceTests
{
    private readonly IMaxioAdvancedBillingClient _maxio = Substitute.For<IMaxioAdvancedBillingClient>();
    private readonly ShopperIdentity _shopper = new("user-1", "demouser@microsoft.com", "demouser@microsoft.com");

    private MaxioSubscriptionBillingService CreateSut()
    {
        var options = Options.Create(new MaxioOptions
        {
            ProductFamilyHandle = "eshop-subscribe"
        });

        return new MaxioSubscriptionBillingService(
            _maxio,
            options,
            new SubscriptionCreationGate(),
            NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    [Fact]
    public async Task ListPlans_FiltersArchivedProducts()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", default).Returns(new List<MaxioProduct>
        {
            new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" },
            new() { Handle = "retired", Name = "Retired", PriceInCents = 100, Interval = 1, IntervalUnit = "month", ArchivedAt = DateTimeOffset.UtcNow }
        });

        var plans = await CreateSut().ListPlansAsync();

        Assert.Single(plans);
        Assert.Equal("eshop-pro", plans[0].Handle);
        Assert.Equal(299.00m, plans[0].Price);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerThenSubscription()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", default).Returns(new List<MaxioProduct>
        {
            new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
        });
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, default).Returns((MaxioCustomer?)null);
        _maxio.CreateCustomerAsync(Arg.Any<MaxioCreateCustomerRequest>(), default)
            .Returns(new MaxioCustomer { Id = 42, Reference = _shopper.UserId, Email = _shopper.Email });
        _maxio.ListCustomerSubscriptionsAsync(42, default).Returns(new List<MaxioSubscription>());
        _maxio.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), default)
            .Returns(new MaxioSubscription
            {
                Id = 99,
                State = "active",
                ProductPriceInCents = 29900,
                CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
                Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan" }
            });

        var result = await CreateSut().SubscribeAsync(_shopper, "eshop-pro");

        Assert.Equal(99, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.ProductHandle);
        Assert.Equal(299.00m, result.Price);
        await _maxio.Received(1).CreateCustomerAsync(
            Arg.Is<MaxioCreateCustomerRequest>(r => r.Customer.Reference == _shopper.UserId),
            default);
        await _maxio.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscriptionRequest>(r =>
                r.Subscription.ProductHandle == "eshop-pro" &&
                r.Subscription.CustomerId == 42 &&
                r.Subscription.PaymentCollectionMethod == "remittance"),
            default);
    }

    [Fact]
    public async Task Subscribe_IsIdempotent_WhenLiveSubscriptionExists()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", default).Returns(new List<MaxioProduct>
        {
            new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
        });
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, default)
            .Returns(new MaxioCustomer { Id = 42, Reference = _shopper.UserId });
        _maxio.ListCustomerSubscriptionsAsync(42, default).Returns(new List<MaxioSubscription>
        {
            new()
            {
                Id = 7,
                State = "active",
                ProductPriceInCents = 29900,
                Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan" }
            }
        });

        var sut = CreateSut();
        var first = await sut.SubscribeAsync(_shopper, "eshop-pro");
        var second = await sut.SubscribeAsync(_shopper, "eshop-pro");

        Assert.Equal(7, first.Id);
        Assert.Equal(7, second.Id);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), default);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomerRequest>(), default);
    }

    [Fact]
    public async Task Subscribe_ReusesCustomer_OnDuplicateReference()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", default).Returns(new List<MaxioProduct>
        {
            new() { Handle = "basic-plan", Name = "Basic", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" }
        });
        _maxio.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, new MaxioCustomer { Id = 5, Reference = _shopper.UserId });
        _maxio.CreateCustomerAsync(Arg.Any<MaxioCreateCustomerRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new MaxioApiException(HttpStatusCode.UnprocessableEntity, "Reference: must be unique"));
        _maxio.ListCustomerSubscriptionsAsync(5, default).Returns(new List<MaxioSubscription>());
        _maxio.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscriptionRequest>(), default)
            .Returns(new MaxioSubscription
            {
                Id = 11,
                State = "active",
                ProductPriceInCents = 2900,
                Product = new MaxioProduct { Handle = "basic-plan", Name = "Basic" }
            });

        var result = await CreateSut().SubscribeAsync(_shopper, "basic-plan");

        Assert.Equal(11, result.Id);
        await _maxio.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscriptionRequest>(r => r.Subscription.CustomerId == 5),
            default);
    }

    [Fact]
    public async Task Subscribe_UnknownPlan_Throws()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", default).Returns(new List<MaxioProduct>
        {
            new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
        });

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateSut().SubscribeAsync(_shopper, "does-not-exist"));
    }

    [Fact]
    public void ResolveBaseUrl_UsesOverrideVerbatim()
    {
        var options = new MaxioOptions { BaseUrl = "https://example.test/v1" };
        Assert.Equal("https://example.test/v1/", options.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_DerivesFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-3" };
        Assert.Equal("https://cp-exp-3.chargify.com/", options.ResolveBaseUrl());
    }
}
