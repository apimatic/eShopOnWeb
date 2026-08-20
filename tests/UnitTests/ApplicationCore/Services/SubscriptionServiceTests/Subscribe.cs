using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class Subscribe
{
    private readonly IMaxioBillingClient _maxio = Substitute.For<IMaxioBillingClient>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();
    private readonly ShopperIdentity _shopper = new("user-1", "demouser@microsoft.com", "demouser@microsoft.com");
    private readonly SubscriptionPlan _proPlan = new("eshop-pro", "Pro Plan", "Pro", 299m, 1, "month");

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenNoneExist()
    {
        var customer = new MaxioCustomer(42, _shopper.Email, SubscriptionService.BuildCustomerReference(_shopper.UserId));
        var created = new ShopperSubscription(99, "active", "eshop-pro", "Pro Plan", 299m, DateTimeOffset.UtcNow.AddMonths(1));

        _maxio.ListProductsInFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionPlan> { _proPlan });
        _maxio.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        _maxio.CreateCustomerAsync(Arg.Any<CreateMaxioCustomerRequest>(), Arg.Any<CancellationToken>())
            .Returns(customer);
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ShopperSubscription?)null);
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<ShopperSubscription>());
        _maxio.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(created);

        var service = CreateService();
        var result = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        await _maxio.Received(1).CreateCustomerAsync(
            Arg.Is<CreateMaxioCustomerRequest>(r => r.Reference == "eshop:user-1" && r.Email == _shopper.Email),
            Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateMaxioSubscriptionRequest>(r =>
                r.CustomerId == 42
                && r.ProductHandle == "eshop-pro"
                && r.Reference == "eshop-sub:user-1:eshop-pro"
                && !string.IsNullOrWhiteSpace(r.UniquenessToken)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoesNotCreateSecondCustomerOrSubscriptionOnRepeatSubscribe()
    {
        var customer = new MaxioCustomer(42, _shopper.Email, "eshop:user-1");
        var existing = new ShopperSubscription(99, "active", "eshop-pro", "Pro Plan", 299m, DateTimeOffset.UtcNow.AddMonths(1));

        _maxio.ListProductsInFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionPlan> { _proPlan });
        _maxio.FindCustomerByReferenceAsync("eshop:user-1", Arg.Any<CancellationToken>())
            .Returns(customer);
        _maxio.FindSubscriptionByReferenceAsync("eshop-sub:user-1:eshop-pro", Arg.Any<CancellationToken>())
            .Returns(existing);

        var service = CreateService();
        var first = await service.SubscribeAsync(_shopper, "eshop-pro");
        var second = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.False(first.Created);
        Assert.False(second.Created);
        Assert.Equal(99, first.Subscription.Id);
        Assert.Equal(99, second.Subscription.Id);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateMaxioCustomerRequest>(), Arg.Any<CancellationToken>());
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoversExistingSubscriptionWhenCreateReturnsConflict()
    {
        var customer = new MaxioCustomer(42, _shopper.Email, "eshop:user-1");
        var existing = new ShopperSubscription(99, "active", "eshop-pro", "Pro Plan", 299m, DateTimeOffset.UtcNow.AddMonths(1));

        _maxio.ListProductsInFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionPlan> { _proPlan });
        _maxio.FindCustomerByReferenceAsync("eshop:user-1", Arg.Any<CancellationToken>())
            .Returns(customer);
        _maxio.FindSubscriptionByReferenceAsync("eshop-sub:user-1:eshop-pro", Arg.Any<CancellationToken>())
            .Returns((ShopperSubscription?)null, existing);
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<ShopperSubscription>());
        _maxio.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns<ShopperSubscription>(_ => throw new MaxioApiException("duplicate", 409));

        var service = CreateService();
        var result = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(99, result.Subscription.Id);
    }

    [Fact]
    public async Task RejectsProductHandleOutsideConfiguredFamily()
    {
        _maxio.ListProductsInFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionPlan> { _proPlan });

        var service = CreateService();
        await Assert.ThrowsAsync<UnknownSubscriptionPlanException>(() => service.SubscribeAsync(_shopper, "other-plan"));
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListMySubscriptionsReturnsEmptyWhenCustomerDoesNotExist()
    {
        _maxio.FindCustomerByReferenceAsync("eshop:user-1", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var service = CreateService();
        var result = await service.ListMySubscriptionsAsync(_shopper);

        Assert.Empty(result);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("demouser@microsoft.com", "demouser@microsoft.com", "demouser", "Customer")]
    [InlineData("Ada Lovelace", "ada@example.com", "Ada", "Lovelace")]
    public void SplitNameUsesEmailLocalPartOrDisplayName(string userName, string email, string expectedFirst, string expectedLast)
    {
        var shopper = new ShopperIdentity("id", email, userName);
        var (first, last) = SubscriptionService.SplitName(shopper);
        Assert.Equal(expectedFirst, first);
        Assert.Equal(expectedLast, last);
    }

    private SubscriptionService CreateService()
    {
        var options = new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "cp-exp-1",
            ProductFamilyHandle = "eshop-subscribe"
        };
        return new SubscriptionService(_maxio, options, _logger);
    }
}
