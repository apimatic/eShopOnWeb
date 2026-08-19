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
    private readonly SubscriptionBillingOptions _options = new() { ProductFamilyHandle = "eshop-subscribe" };
    private readonly ShopperProfile _shopper = new("user-1", "demouser@microsoft.com", "demouser@microsoft.com");

    private SubscriptionBillingService CreateService() =>
        new(_maxio, _logger, _options, new SubscriptionIdempotencyGate());

    private SubscriptionPlan FamilyPlan(string handle = "eshop-pro") => new()
    {
        Id = 1,
        Handle = handle,
        Name = "Pro Plan",
        Price = 299m,
        Interval = 1,
        IntervalUnit = "month",
        ProductFamilyHandle = "eshop-subscribe"
    };

    [Fact]
    public async Task CreatesCustomerThenSubscriptionWhenShopperIsNew()
    {
        _maxio.ReadProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(FamilyPlan());
        _maxio.ReadCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);
        _maxio.CreateCustomerAsync("Demouser", "Subscriber", "demouser@microsoft.com", "user-1", Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = "user-1", Email = "demouser@microsoft.com" });
        _maxio.FindSubscriptionByReferenceAsync("user-1:eshop-pro", Arg.Any<CancellationToken>()).Returns((ShopperSubscription?)null);
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<ShopperSubscription>());
        _maxio.CreateSubscriptionAsync(42, "eshop-pro", "user-1:eshop-pro", "remittance", Arg.Any<CancellationToken>())
            .Returns(new ShopperSubscription
            {
                Id = 99,
                ProductHandle = "eshop-pro",
                ProductName = "Pro Plan",
                Price = 299m,
                State = "active",
                NextBillingDate = DateTimeOffset.UtcNow.AddMonths(1)
            });

        var result = await CreateService().SubscribeAsync(_shopper, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        await _maxio.Received(1).CreateCustomerAsync("Demouser", "Subscriber", "demouser@microsoft.com", "user-1", Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(42, "eshop-pro", "user-1:eshop-pro", "remittance", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsExistingLiveSubscriptionWithoutCreatingAnother()
    {
        var existing = new ShopperSubscription
        {
            Id = 7,
            ProductHandle = "eshop-pro",
            ProductName = "Pro Plan",
            Price = 299m,
            State = "active",
            CustomerId = 42
        };

        _maxio.ReadProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(FamilyPlan());
        _maxio.ReadCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = "user-1" });
        _maxio.FindSubscriptionByReferenceAsync("user-1:eshop-pro", Arg.Any<CancellationToken>()).Returns(existing);

        var first = await CreateService().SubscribeAsync(_shopper, "eshop-pro");
        var second = await CreateService().SubscribeAsync(_shopper, "eshop-pro");

        Assert.False(first.Created);
        Assert.False(second.Created);
        Assert.Equal(7, first.Subscription.Id);
        Assert.Equal(7, second.Subscription.Id);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoversWhenCreateSubscriptionReturns422ForDuplicateReference()
    {
        var recovered = new ShopperSubscription
        {
            Id = 15,
            ProductHandle = "eshop-pro",
            State = "active",
            CustomerId = 42
        };

        _maxio.ReadProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(FamilyPlan());
        _maxio.ReadCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = "user-1" });
        _maxio.FindSubscriptionByReferenceAsync("user-1:eshop-pro", Arg.Any<CancellationToken>())
            .Returns((ShopperSubscription?)null, recovered);
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<ShopperSubscription>());
        _maxio.CreateSubscriptionAsync(42, "eshop-pro", "user-1:eshop-pro", "remittance", Arg.Any<CancellationToken>())
            .Returns<ShopperSubscription>(_ => throw new MaxioApiException(422, "Reference has already been taken"));

        var result = await CreateService().SubscribeAsync(_shopper, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(15, result.Subscription.Id);
    }

    [Fact]
    public async Task RecoversWhenCreateCustomerReturns422ForDuplicateReference()
    {
        _maxio.ReadProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(FamilyPlan());
        _maxio.ReadCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null, new BillingCustomer { Id = 42, Reference = "user-1" });
        _maxio.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), "user-1", Arg.Any<CancellationToken>())
            .Returns<BillingCustomer>(_ => throw new MaxioApiException(422, "Reference: must be unique"));
        _maxio.FindSubscriptionByReferenceAsync("user-1:eshop-pro", Arg.Any<CancellationToken>()).Returns((ShopperSubscription?)null);
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new List<ShopperSubscription>());
        _maxio.CreateSubscriptionAsync(42, "eshop-pro", "user-1:eshop-pro", "remittance", Arg.Any<CancellationToken>())
            .Returns(new ShopperSubscription { Id = 3, ProductHandle = "eshop-pro", State = "active" });

        var result = await CreateService().SubscribeAsync(_shopper, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(3, result.Subscription.Id);
    }

    [Fact]
    public async Task ThrowsWhenPlanIsNotInConfiguredFamily()
    {
        _maxio.ReadProductByHandleAsync("other-plan", Arg.Any<CancellationToken>()).Returns(new SubscriptionPlan
        {
            Handle = "other-plan",
            ProductFamilyHandle = "someone-elses-family"
        });

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService().SubscribeAsync(_shopper, "other-plan"));
    }

    [Fact]
    public async Task ThrowsWhenPlanDoesNotExist()
    {
        _maxio.ReadProductByHandleAsync("missing", Arg.Any<CancellationToken>()).Returns((SubscriptionPlan?)null);

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateService().SubscribeAsync(_shopper, "missing"));
    }

    [Fact]
    public async Task ThrowsWhenProductHandleIsBlank()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().SubscribeAsync(_shopper, "  "));
    }

    [Fact]
    public void SplitsEmailLocalPartIntoCustomerName()
    {
        var (first, last) = SubscriptionBillingService.SplitDisplayName(
            new ShopperProfile("id", "jane.doe@microsoft.com", "jane.doe@microsoft.com"));

        Assert.Equal("Jane", first);
        Assert.Equal("Doe", last);
    }
}

public class ListPlansAndSubscriptions
{
    private readonly IMaxioAdvancedBillingClient _maxio = Substitute.For<IMaxioAdvancedBillingClient>();
    private readonly IAppLogger<SubscriptionBillingService> _logger = Substitute.For<IAppLogger<SubscriptionBillingService>>();
    private readonly SubscriptionBillingOptions _options = new() { ProductFamilyHandle = "eshop-subscribe" };

    private SubscriptionBillingService CreateService() =>
        new(_maxio, _logger, _options, new SubscriptionIdempotencyGate());

    [Fact]
    public async Task ListAvailablePlansRequestsConfiguredFamily()
    {
        _maxio.ListProductsForProductFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<SubscriptionPlan>
            {
                new() { Handle = "eshop-pro", Name = "Pro", Price = 299m, Interval = 1, IntervalUnit = "month" },
                new() { Handle = "basic-plan", Name = "Basic", Price = 29m, Interval = 1, IntervalUnit = "month" }
            });

        var plans = await CreateService().ListAvailablePlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("basic-plan", plans[0].Handle);
        Assert.Equal("eshop-pro", plans[1].Handle);
    }

    [Fact]
    public async Task ListMySubscriptionsReturnsEmptyWhenCustomerDoesNotExist()
    {
        _maxio.ReadCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>()).Returns((BillingCustomer?)null);

        var subscriptions = await CreateService().ListMySubscriptionsAsync(
            new ShopperProfile("user-1", "a@b.com", "a@b.com"));

        Assert.Empty(subscriptions);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThrowsWhenProductFamilyHandleIsMissing()
    {
        var service = new SubscriptionBillingService(
            _maxio,
            _logger,
            new SubscriptionBillingOptions(),
            new SubscriptionIdempotencyGate());

        await Assert.ThrowsAsync<BillingConfigurationException>(() => service.ListAvailablePlansAsync());
    }
}
