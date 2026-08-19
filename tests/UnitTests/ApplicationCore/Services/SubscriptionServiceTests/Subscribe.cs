using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class Subscribe
{
    private readonly ISubscriptionBillingGateway _gateway = Substitute.For<ISubscriptionBillingGateway>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();
    private readonly IMaxioSettings _settings = Substitute.For<IMaxioSettings>();
    private readonly ShopperIdentity _shopper = new("user-1", "demouser@microsoft.com", "demouser@microsoft.com");
    private readonly SubscriptionPlan _proPlan = new(1, "eshop-pro", "Pro Plan", "Pro", 299m, 1, "month", "eshop-subscribe");

    public Subscribe()
    {
        _settings.ProductFamilyHandle.Returns("eshop-subscribe");
        _gateway.ListPlansAsync("eshop-subscribe", Arg.Any<CancellationToken>()).Returns(new List<SubscriptionPlan> { _proPlan });
    }

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenNoneExist()
    {
        _gateway.FindCustomerByReferenceAsync("eshop:user-1", Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);
        _gateway.CreateCustomerAsync(_shopper, "eshop:user-1", Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(10, "eshop:user-1", _shopper.Email));
        _gateway.ListCustomerSubscriptionsAsync(10, Arg.Any<CancellationToken>()).Returns(new List<CustomerSubscription>());
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((CustomerSubscription?)null);

        var created = new CustomerSubscription(99, "active", "eshop-pro", "Pro Plan", 299m, DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow, "ref");
        _gateway.CreateSubscriptionAsync(10, "eshop-pro", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(created);

        var service = CreateService();
        var result = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        await _gateway.Received(1).CreateCustomerAsync(_shopper, "eshop:user-1", Arg.Any<CancellationToken>());
        await _gateway.Received(1).CreateSubscriptionAsync(10, "eshop-pro", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsExistingLiveSubscriptionWithoutCreatingAnother()
    {
        _gateway.FindCustomerByReferenceAsync("eshop:user-1", Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(10, "eshop:user-1", _shopper.Email));
        var existing = new CustomerSubscription(42, "active", "eshop-pro", "Pro Plan", 299m, DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow, "ref");
        _gateway.ListCustomerSubscriptionsAsync(10, Arg.Any<CancellationToken>()).Returns(new List<CustomerSubscription> { existing });

        var service = CreateService();
        var first = await service.SubscribeAsync(_shopper, "eshop-pro");
        var second = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.False(first.Created);
        Assert.False(second.Created);
        Assert.Equal(42, first.Subscription.Id);
        Assert.Equal(42, second.Subscription.Id);
        await _gateway.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _gateway.DidNotReceive().CreateCustomerAsync(Arg.Any<ShopperIdentity>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoversExistingSubscriptionWhenMaxioReportsDuplicate()
    {
        _gateway.FindCustomerByReferenceAsync("eshop:user-1", Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(10, "eshop:user-1", _shopper.Email));
        _gateway.ListCustomerSubscriptionsAsync(10, Arg.Any<CancellationToken>())
            .Returns(new List<CustomerSubscription>(), new List<CustomerSubscription>
            {
                new(7, "active", "eshop-pro", "Pro Plan", 299m, DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow, "ref")
            });
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((CustomerSubscription?)null);
        _gateway.CreateSubscriptionAsync(10, "eshop-pro", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new MaxioApiException("duplicate") { StatusCode = 409 });

        var service = CreateService();
        var result = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(7, result.Subscription.Id);
    }

    [Fact]
    public async Task RejectsUnknownPlanHandle()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidSubscriptionRequestException>(
            () => service.SubscribeAsync(_shopper, "not-a-plan"));
    }

    [Fact]
    public async Task ListMySubscriptionsReturnsEmptyWhenCustomerDoesNotExist()
    {
        _gateway.FindCustomerByReferenceAsync("eshop:user-1", Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);

        var service = CreateService();
        var result = await service.ListMySubscriptionsAsync(_shopper);

        Assert.Empty(result);
        await _gateway.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private SubscriptionService CreateService() => new(_gateway, _logger, _settings);
}
