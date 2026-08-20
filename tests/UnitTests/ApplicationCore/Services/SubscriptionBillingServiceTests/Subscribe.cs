using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingServiceTests;

public class Subscribe
{
    private readonly IMaxioBillingGateway _maxio = Substitute.For<IMaxioBillingGateway>();
    private readonly IAppLogger<SubscriptionBillingService> _logger = Substitute.For<IAppLogger<SubscriptionBillingService>>();
    private readonly ShopperIdentity _shopper = new("user-123", "demouser@microsoft.com", "demouser@microsoft.com");
    private readonly SubscriptionPlan _proPlan = new("eshop-pro", "Pro Plan", "Pro", 299m, 1, "month", "eshop-subscribe", false);
    private readonly BillingCustomer _customer = new(42, "eshop-user:user-123", "demouser@microsoft.com");

    private SubscriptionBillingService CreateService() => new(_maxio, _logger);

    [Fact]
    public async Task ReturnsInvalidWhenProductHandleIsMissing()
    {
        var service = CreateService();

        var result = await service.SubscribeAsync(_shopper, "  ", CancellationToken.None);

        Assert.Equal(Ardalis.Result.ResultStatus.Invalid, result.Status);
        await _maxio.DidNotReceiveWithAnyArgs().GetPlanByHandleAsync(default!, default);
    }

    [Fact]
    public async Task ReturnsNotFoundWhenPlanIsOutsideConfiguredFamily()
    {
        _maxio.GetPlanByHandleAsync("other-plan", Arg.Any<CancellationToken>()).Returns((SubscriptionPlan?)null);
        var service = CreateService();

        var result = await service.SubscribeAsync(_shopper, "other-plan", CancellationToken.None);

        Assert.Equal(Ardalis.Result.ResultStatus.NotFound, result.Status);
        await _maxio.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default!, default);
    }

    [Fact]
    public async Task ReusesExistingCustomerAndOpenSubscription()
    {
        var existing = new ShopperSubscription(99, "ref", "active", "eshop-pro", "Pro Plan", 299m, DateTimeOffset.UtcNow.AddMonths(1));
        _maxio.GetPlanByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(_proPlan);
        _maxio.FindCustomerByReferenceAsync("eshop-user:user-123", Arg.Any<CancellationToken>())
            .Returns(_customer);
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<ShopperSubscription> { existing });

        var service = CreateService();
        var result = await service.SubscribeAsync(_shopper, "eshop-pro", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(99, result.Value.Id);
        await _maxio.DidNotReceiveWithAnyArgs().CreateCustomerAsync(default!, default!, default);
        await _maxio.DidNotReceiveWithAnyArgs().CreateSubscriptionAsync(default!, default);
    }

    [Fact]
    public async Task CreatesCustomerThenSubscriptionWhenNoneExist()
    {
        var created = new ShopperSubscription(7, "ref", "active", "eshop-pro", "Pro Plan", 299m, DateTimeOffset.UtcNow.AddMonths(1));
        _maxio.GetPlanByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(_proPlan);
        _maxio.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);
        _maxio.CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_customer);
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<ShopperSubscription>());
        _maxio.CreateSubscriptionAsync(Arg.Any<NewBillingSubscription>(), Arg.Any<CancellationToken>())
            .Returns(created);

        var service = CreateService();
        var result = await service.SubscribeAsync(_shopper, "eshop-pro", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Value.Id);
        await _maxio.Received(1).CreateCustomerAsync(
            Arg.Is<NewBillingCustomer>(c => c.Reference == "eshop-user:user-123" && c.Email == _shopper.Email),
            "eshop-cust-user-123",
            Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(
            Arg.Is<NewBillingSubscription>(s => s.ProductHandle == "eshop-pro" && s.CustomerId == 42 && s.Reference == "eshop-sub:user-123:eshop-pro" && s.PaymentCollectionMethod == "remittance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListMySubscriptionsReturnsEmptyWhenCustomerDoesNotExist()
    {
        _maxio.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);
        var service = CreateService();

        var result = await service.ListMySubscriptionsAsync(_shopper, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
        await _maxio.DidNotReceiveWithAnyArgs().ListCustomerSubscriptionsAsync(default, default);
    }

    [Fact]
    public async Task ListPlansReturnsGatewayPlans()
    {
        _maxio.ListAvailablePlansAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionPlan> { _proPlan });
        var service = CreateService();

        var result = await service.ListPlansAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("eshop-pro", result.Value[0].Handle);
    }
}
