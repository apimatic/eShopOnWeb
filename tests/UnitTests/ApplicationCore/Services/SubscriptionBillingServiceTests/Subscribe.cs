using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingServiceTests;

public class Subscribe
{
    private readonly IAdvancedBillingGateway _gateway = Substitute.For<IAdvancedBillingGateway>();
    private readonly IBillingCatalogSettings _settings = Substitute.For<IBillingCatalogSettings>();
    private readonly IAppLogger<SubscriptionBillingService> _logger = Substitute.For<IAppLogger<SubscriptionBillingService>>();
    private readonly ShopperIdentity _shopper = new()
    {
        UserId = "user-1",
        Email = "demouser@microsoft.com",
        UserName = "demouser@microsoft.com"
    };

    public Subscribe()
    {
        _settings.ProductFamilyHandle.Returns("eshop-subscribe");
        _gateway.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionPlan>
            {
                new()
                {
                    Handle = "eshop-pro",
                    Name = "Pro Plan",
                    PriceInCents = 29900,
                    Interval = 1,
                    IntervalUnit = "month"
                }
            });
    }

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenShopperIsNew()
    {
        _gateway.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);
        _gateway.CreateCustomerAsync(Arg.Any<CreateBillingCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = _shopper.UserId, Email = _shopper.Email });
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ShopperSubscription?)null);
        _gateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<ShopperSubscription>());
        _gateway.CreateSubscriptionAsync(Arg.Any<CreateBillingSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new ShopperSubscription
            {
                Id = 1001,
                State = "active",
                ProductHandle = "eshop-pro",
                ProductName = "Pro Plan",
                PriceInCents = 29900,
                NextBillingDate = DateTimeOffset.UtcNow.AddMonths(1)
            });

        var service = CreateService();
        var result = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(1001, result.Subscription.Id);
        await _gateway.Received(1).CreateCustomerAsync(
            Arg.Is<CreateBillingCustomer>(c => c.Reference == _shopper.UserId && c.Email == _shopper.Email),
            Arg.Any<CancellationToken>());
        await _gateway.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateBillingSubscription>(s =>
                s.CustomerId == 42 &&
                s.ProductHandle == "eshop-pro" &&
                s.PaymentCollectionMethod == "remittance" &&
                s.Reference == "user-1:eshop-pro"),
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
            ProductName = "Pro Plan",
            PriceInCents = 29900
        };

        _gateway.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = _shopper.UserId });
        _gateway.FindSubscriptionByReferenceAsync("user-1:eshop-pro", Arg.Any<CancellationToken>())
            .Returns(existing);

        var service = CreateService();
        var result = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(77, result.Subscription.Id);
        await _gateway.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateBillingCustomer>(), Arg.Any<CancellationToken>());
        await _gateway.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateBillingSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsUnknownProductHandle()
    {
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => service.SubscribeAsync(_shopper, "not-a-plan"));

        Assert.Equal(400, ex.StatusCode);
        await _gateway.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateBillingSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReusesCustomerCreatedInARace()
    {
        _gateway.FindCustomerByReferenceAsync(_shopper.UserId, Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null, new BillingCustomer { Id = 42, Reference = _shopper.UserId });
        _gateway.CreateCustomerAsync(Arg.Any<CreateBillingCustomer>(), Arg.Any<CancellationToken>())
            .Returns<BillingCustomer>(_ => throw new AdvancedBillingException("reference already taken", 422));
        _gateway.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ShopperSubscription?)null);
        _gateway.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<ShopperSubscription>());
        _gateway.CreateSubscriptionAsync(Arg.Any<CreateBillingSubscription>(), Arg.Any<CancellationToken>())
            .Returns(new ShopperSubscription
            {
                Id = 9,
                State = "active",
                ProductHandle = "eshop-pro",
                ProductName = "Pro Plan"
            });

        var service = CreateService();
        var result = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(9, result.Subscription.Id);
    }

    private SubscriptionBillingService CreateService() =>
        new(_gateway, _settings, _logger);
}
