using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Services;

public class SubscriptionServiceTests
{
    private const string ProductFamilyHandle = "eshop-subscribe";

    private readonly IMaxioClient _maxioClient = Substitute.For<IMaxioClient>();
    private readonly MaxioOptions _options = new() { ProductFamilyHandle = ProductFamilyHandle };

    private SubscriptionService CreateService()
    {
        return new SubscriptionService(
            _maxioClient,
            Options.Create(_options),
            NullLogger<SubscriptionService>.Instance);
    }

    private static MaxioProductFamily Family() => new() { Id = 3026730, Handle = ProductFamilyHandle, Name = "eShopSubscribe" };

    private static MaxioProduct Product(int id, string handle, string name, int priceInCents) => new()
    {
        Id = id,
        Handle = handle,
        Name = name,
        PriceInCents = priceInCents,
        Interval = 1,
        IntervalUnit = "month",
        ProductFamily = Family()
    };

    private static MaxioSubscription Subscription(int id, string state, MaxioProduct product) => new()
    {
        Id = id,
        State = state,
        ProductPriceInCents = product.PriceInCents,
        Product = product,
        PaymentCollectionMethod = "remittance",
        CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task ListPlansAsync_ReturnsPlansFromConfiguredFamily()
    {
        _maxioClient.GetProductFamilyByHandleAsync(ProductFamilyHandle, Arg.Any<CancellationToken>())
            .Returns(Family());
        _maxioClient.ListProductsAsync(3026730, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                Product(7130997, "eshop-pro", "Pro Plan", 29900),
                Product(7130998, "basic-plan", "Basic Plan", 2900)
            });

        var service = CreateService();
        var plans = await service.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("eshop-pro", plans[0].Handle);
        Assert.Equal(299.00m, plans[0].Price);
        Assert.Equal("basic-plan", plans[1].Handle);
        Assert.Equal(29.00m, plans[1].Price);
    }

    [Fact]
    public async Task ListPlansAsync_ThrowsWhenFamilyNotFound()
    {
        _maxioClient.GetProductFamilyByHandleAsync(ProductFamilyHandle, Arg.Any<CancellationToken>())
            .Returns((MaxioProductFamily?)null);

        var service = CreateService();

        await Assert.ThrowsAsync<MaxioApiException>(() => service.ListPlansAsync());
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscriptionWhenNoneExist()
    {
        _maxioClient.GetCustomerByReferenceAsync("demouser@microsoft.com", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        _maxioClient.CreateCustomerAsync(Arg.Any<CreateCustomerRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 100, Email = "demouser@microsoft.com", Reference = "demouser@microsoft.com" });
        _maxioClient.ListCustomerSubscriptionsAsync(100, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _maxioClient.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(500, "active", Product(7130997, "eshop-pro", "Pro Plan", 29900)));

        var service = CreateService();
        var subscription = await service.SubscribeAsync("demouser@microsoft.com", "eshop-pro");

        Assert.Equal(500, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(299.00m, subscription.Price);

        await _maxioClient.Received(1).CreateCustomerAsync(Arg.Any<CreateCustomerRequest>(), Arg.Any<CancellationToken>());
        await _maxioClient.Received(1).CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ReusesExistingCustomer()
    {
        _maxioClient.GetCustomerByReferenceAsync("demouser@microsoft.com", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 100, Email = "demouser@microsoft.com", Reference = "demouser@microsoft.com" });
        _maxioClient.ListCustomerSubscriptionsAsync(100, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _maxioClient.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(500, "active", Product(7130997, "eshop-pro", "Pro Plan", 29900)));

        var service = CreateService();
        await service.SubscribeAsync("demouser@microsoft.com", "eshop-pro");

        await _maxioClient.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateCustomerRequest>(), Arg.Any<CancellationToken>());
        await _maxioClient.Received(1).CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_IsIdempotent_ReturnsExistingActiveSubscription()
    {
        var existing = Subscription(500, "active", Product(7130997, "eshop-pro", "Pro Plan", 29900));
        _maxioClient.GetCustomerByReferenceAsync("demouser@microsoft.com", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 100, Email = "demouser@microsoft.com", Reference = "demouser@microsoft.com" });
        _maxioClient.ListCustomerSubscriptionsAsync(100, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { existing });

        var service = CreateService();
        var subscription = await service.SubscribeAsync("demouser@microsoft.com", "eshop-pro");

        Assert.Equal(500, subscription.Id);
        await _maxioClient.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_AllowsSubscriptionToDifferentPlan()
    {
        var existing = Subscription(500, "active", Product(7130997, "eshop-pro", "Pro Plan", 29900));
        _maxioClient.GetCustomerByReferenceAsync("demouser@microsoft.com", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 100, Email = "demouser@microsoft.com", Reference = "demouser@microsoft.com" });
        _maxioClient.ListCustomerSubscriptionsAsync(100, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription> { existing });
        _maxioClient.CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Subscription(501, "active", Product(7130998, "basic-plan", "Basic Plan", 2900)));

        var service = CreateService();
        var subscription = await service.SubscribeAsync("demouser@microsoft.com", "basic-plan");

        Assert.Equal(501, subscription.Id);
        Assert.Equal("basic-plan", subscription.PlanHandle);
        await _maxioClient.Received(1).CreateSubscriptionAsync(Arg.Any<CreateSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ReturnsEmptyWhenNoCustomerExists()
    {
        _maxioClient.GetCustomerByReferenceAsync("demouser@microsoft.com", Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var service = CreateService();
        var subscriptions = await service.ListSubscriptionsAsync("demouser@microsoft.com");

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ReturnsCustomerSubscriptions()
    {
        _maxioClient.GetCustomerByReferenceAsync("demouser@microsoft.com", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 100, Email = "demouser@microsoft.com", Reference = "demouser@microsoft.com" });
        _maxioClient.ListCustomerSubscriptionsAsync(100, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                Subscription(500, "active", Product(7130997, "eshop-pro", "Pro Plan", 29900))
            });

        var service = CreateService();
        var subscriptions = await service.ListSubscriptionsAsync("demouser@microsoft.com");

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(500, subscription.Id);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
    }
}
