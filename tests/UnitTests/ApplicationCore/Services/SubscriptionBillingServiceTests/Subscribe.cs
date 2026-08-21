using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingServiceTests;

public class Subscribe
{
    private readonly IMaxioAdvancedBillingClient _maxio = Substitute.For<IMaxioAdvancedBillingClient>();
    private readonly IAppLogger<SubscriptionBillingService> _logger = Substitute.For<IAppLogger<SubscriptionBillingService>>();
    private readonly ShopperIdentity _shopper = new("user-1", "demouser@microsoft.com", "demouser@microsoft.com");
    private readonly SubscriptionPlan _proPlan = new()
    {
        Id = 1,
        Handle = "eshop-pro",
        Name = "Pro Plan",
        PriceInCents = 29900,
        Interval = 1,
        IntervalUnit = "month",
        ProductFamilyHandle = "eshop-subscribe"
    };

    public Subscribe()
    {
        _maxio.ProductFamilyHandle.Returns("eshop-subscribe");
        _maxio.ReadProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(_proPlan);
    }

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenNoneExist()
    {
        _maxio.ReadCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);
        _maxio.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), _shopper.UserId, Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = _shopper.UserId, Email = _shopper.Email });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(Array.Empty<ShopperSubscription>());
        _maxio.CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<string>(), "remittance", Arg.Any<CancellationToken>())
            .Returns(new ShopperSubscription
            {
                Id = 99,
                State = "active",
                ProductHandle = "eshop-pro",
                ProductName = "Pro Plan",
                ProductPriceInCents = 29900
            });

        var service = new SubscriptionBillingService(_maxio, _logger);
        var result = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        await _maxio.Received(1).CreateCustomerAsync("demouser", "eShopOnWeb", _shopper.Email, _shopper.UserId, Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(
            42,
            "eshop-pro",
            SubscriptionBillingService.BuildSubscriptionReference(_shopper.UserId, "eshop-pro"),
            "remittance",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsExistingLiveSubscriptionWithoutCreatingAnother()
    {
        var existing = new ShopperSubscription
        {
            Id = 77,
            State = "active",
            ProductHandle = "eshop-pro",
            ProductPriceInCents = 29900
        };
        _maxio.ReadCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = _shopper.UserId });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<ShopperSubscription> { existing });

        var service = new SubscriptionBillingService(_maxio, _logger);
        var result = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(77, result.Subscription.Id);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoversWhenCreateSubscriptionReturns422BecauseOfARace()
    {
        var recovered = new ShopperSubscription
        {
            Id = 88,
            State = "active",
            ProductHandle = "eshop-pro",
            ProductPriceInCents = 29900
        };
        var reference = SubscriptionBillingService.BuildSubscriptionReference(_shopper.UserId, "eshop-pro");

        _maxio.ReadCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = _shopper.UserId });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(Array.Empty<ShopperSubscription>());
        _maxio.CreateSubscriptionAsync(42, "eshop-pro", reference, "remittance", Arg.Any<CancellationToken>())
            .Returns<ShopperSubscription>(_ => throw new MaxioApiException("taken", 422));
        _maxio.FindSubscriptionByReferenceAsync(reference, Arg.Any<CancellationToken>()).Returns(recovered);

        var service = new SubscriptionBillingService(_maxio, _logger);
        var result = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(88, result.Subscription.Id);
    }

    [Fact]
    public async Task ThrowsWhenPlanIsNotInTheConfiguredFamily()
    {
        _maxio.ReadProductByHandleAsync("other-plan", Arg.Any<CancellationToken>()).Returns(new SubscriptionPlan
        {
            Id = 2,
            Handle = "other-plan",
            Name = "Other",
            ProductFamilyHandle = "someone-else"
        });

        var service = new SubscriptionBillingService(_maxio, _logger);

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() => service.SubscribeAsync(_shopper, "other-plan"));
    }

    [Fact]
    public async Task ListMySubscriptionsReturnsEmptyWhenCustomerDoesNotExist()
    {
        _maxio.ReadCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);

        var service = new SubscriptionBillingService(_maxio, _logger);
        var result = await service.ListMySubscriptionsAsync(_shopper);

        Assert.Empty(result);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveBaseAddressUsesSubdomainTemplateFromTheSpec()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-2" };
        Assert.Equal("https://cp-exp-2.chargify.com/", options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void ResolveBaseAddressPrefersExplicitBaseUrl()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored",
            BaseUrl = "https://example.test/maxio"
        };
        Assert.Equal("https://example.test/maxio/", options.ResolveBaseAddress().ToString());
    }
}
