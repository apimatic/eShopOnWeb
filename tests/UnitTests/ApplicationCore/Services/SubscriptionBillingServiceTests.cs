using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class SubscriptionBillingServiceTests
{
    private readonly IMaxioAdvancedBillingClient _maxio = Substitute.For<IMaxioAdvancedBillingClient>();
    private readonly IAppLogger<SubscriptionBillingService> _logger = Substitute.For<IAppLogger<SubscriptionBillingService>>();
    private readonly SubscriptionBillingService _sut;

    public SubscriptionBillingServiceTests()
    {
        _sut = new SubscriptionBillingService(_maxio, _logger, "eshop-subscribe");
    }

    [Fact]
    public async Task ListPlansAsync_ReturnsFamilyProducts()
    {
        var plans = new List<SubscriptionPlan>
        {
            new() { Handle = "eshop-pro", Name = "Pro", PriceInCents = 29900 }
        };
        _maxio.ListProductsForProductFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>()).Returns(plans);

        var result = await _sut.ListPlansAsync();

        Assert.Single(result);
        Assert.Equal("eshop-pro", result[0].Handle);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription()
    {
        var shopper = new ShopperProfile { UserId = "user-1", Email = "demouser@microsoft.com", UserName = "demouser@microsoft.com" };
        _maxio.ListProductsForProductFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionPlan> { new() { Handle = "eshop-pro", Name = "Pro", PriceInCents = 29900 } });
        _maxio.ReadCustomerByReferenceAsync(MaxioReference.ForCustomer("user-1"), Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);
        _maxio.CreateCustomerAsync(Arg.Any<CreateBillingCustomerRequest>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = MaxioReference.ForCustomer("user-1") });
        _maxio.FindSubscriptionByReferenceAsync(MaxioReference.ForSubscription("user-1", "eshop-pro"), Arg.Any<CancellationToken>())
            .Returns((ShopperSubscription?)null);
        _maxio.CreateSubscriptionAsync(Arg.Any<CreateBillingSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ShopperSubscription { Id = 99, ProductHandle = "eshop-pro", State = "active", PriceInCents = 29900 });

        var result = await _sut.SubscribeAsync(new SubscribeToPlanRequest { Shopper = shopper, ProductHandle = "eshop-pro" });

        Assert.False(result.AlreadySubscribed);
        Assert.Equal(99, result.Subscription.Id);
        await _maxio.Received(1).CreateCustomerAsync(
            Arg.Is<CreateBillingCustomerRequest>(c => c.Reference == MaxioReference.ForCustomer("user-1")),
            Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateBillingSubscriptionRequest>(s =>
                s.ProductHandle == "eshop-pro" &&
                s.CustomerId == 42 &&
                s.Reference == MaxioReference.ForSubscription("user-1", "eshop-pro") &&
                s.PaymentCollectionMethod == "remittance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_IsIdempotentWhenSubscriptionAlreadyExists()
    {
        var shopper = new ShopperProfile { UserId = "user-1", Email = "demouser@microsoft.com", UserName = "demouser@microsoft.com" };
        _maxio.ListProductsForProductFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionPlan> { new() { Handle = "eshop-pro", Name = "Pro", PriceInCents = 29900 } });
        _maxio.ReadCustomerByReferenceAsync(MaxioReference.ForCustomer("user-1"), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = MaxioReference.ForCustomer("user-1") });
        _maxio.FindSubscriptionByReferenceAsync(MaxioReference.ForSubscription("user-1", "eshop-pro"), Arg.Any<CancellationToken>())
            .Returns(new ShopperSubscription { Id = 99, ProductHandle = "eshop-pro", State = "active" });

        var result = await _sut.SubscribeAsync(new SubscribeToPlanRequest { Shopper = shopper, ProductHandle = "eshop-pro" });

        Assert.True(result.AlreadySubscribed);
        Assert.Equal(99, result.Subscription.Id);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateBillingSubscriptionRequest>(), Arg.Any<CancellationToken>());
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateBillingCustomerRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsWhenPlanIsUnknown()
    {
        var shopper = new ShopperProfile { UserId = "user-1", Email = "a@b.com", UserName = "a@b.com" };
        _maxio.ListProductsForProductFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionPlan> { new() { Handle = "eshop-pro" } });

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() =>
            _sut.SubscribeAsync(new SubscribeToPlanRequest { Shopper = shopper, ProductHandle = "not-a-plan" }));
    }

    [Fact]
    public async Task ListMySubscriptionsAsync_ReturnsEmptyWhenCustomerDoesNotExist()
    {
        _maxio.ReadCustomerByReferenceAsync(MaxioReference.ForCustomer("user-1"), Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);

        var result = await _sut.ListMySubscriptionsAsync("user-1");

        Assert.Empty(result);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
