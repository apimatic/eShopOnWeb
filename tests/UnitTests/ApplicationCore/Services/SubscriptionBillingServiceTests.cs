using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class SubscriptionBillingServiceTests
{
    private const string UserId = "user-1";
    private const string Family = "eshop-subscribe";
    private readonly IMaxioAdvancedBillingClient _maxio = Substitute.For<IMaxioAdvancedBillingClient>();
    private readonly IAppLogger<SubscriptionBillingService> _logger = Substitute.For<IAppLogger<SubscriptionBillingService>>();
    private readonly MaxioSettings _settings = new()
    {
        ApiKey = "test-key",
        Subdomain = "test-site",
        ProductFamilyHandle = Family
    };

    private SubscriptionBillingService CreateService() => new(_maxio, _settings, _logger);

    [Fact]
    public async Task ListPlansAsync_OmitsArchivedProducts()
    {
        _maxio.ListProductsForFamilyAsync(Family, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                new() { Handle = "eshop-pro", Name = "Pro", PriceInCents = 29900, Interval = 1, IntervalUnit = "month", ProductFamilyHandle = Family },
                new() { Handle = "old-plan", Name = "Old", PriceInCents = 100, Archived = true, ProductFamilyHandle = Family }
            });

        var plans = await CreateService().ListPlansAsync(default);

        Assert.Single(plans);
        Assert.Equal("eshop-pro", plans[0].Handle);
        Assert.Equal(299m, plans[0].Price);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerWhenMissingThenEnrolls()
    {
        _maxio.ListProductsForFamilyAsync(Family, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month", ProductFamilyHandle = Family }
            });
        _maxio.GetCustomerByReferenceAsync(SubscriptionBillingService.CustomerReferenceFor(UserId), Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);
        _maxio.CreateCustomerAsync(Arg.Any<CreateMaxioCustomerRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = SubscriptionBillingService.CustomerReferenceFor(UserId) });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioSubscription?)null);
        _maxio.CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription
            {
                Id = 99,
                State = "active",
                ProductHandle = "eshop-pro",
                ProductName = "Pro Plan",
                ProductPriceInCents = 29900,
                CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1)
            });

        var result = await CreateService().SubscribeAsync(new SubscribeToPlanRequest
        {
            ShopperUserId = UserId,
            Email = "demouser@microsoft.com",
            FirstName = "Demo",
            LastName = "User",
            ProductHandle = "eshop-pro"
        }, default);

        Assert.True(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal(299m, result.Subscription.Price);
        await _maxio.Received(1).CreateCustomerAsync(
            Arg.Is<CreateMaxioCustomerRequest>(c => c.Reference == "eshop:user-1" && c.Email == "demouser@microsoft.com"),
            Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(
            Arg.Is<CreateMaxioSubscriptionRequest>(s => s.CustomerId == 42 && s.ProductHandle == "eshop-pro" && s.PaymentCollectionMethod == "remittance"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_DoesNotCreateSecondCustomerOrSubscriptionOnRetry()
    {
        _maxio.ListProductsForFamilyAsync(Family, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, ProductFamilyHandle = Family }
            });
        _maxio.GetCustomerByReferenceAsync(SubscriptionBillingService.CustomerReferenceFor(UserId), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = SubscriptionBillingService.CustomerReferenceFor(UserId) });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                new()
                {
                    Id = 99,
                    State = "active",
                    ProductHandle = "eshop-pro",
                    ProductName = "Pro Plan",
                    ProductPriceInCents = 29900,
                    CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1)
                }
            });

        var result = await CreateService().SubscribeAsync(new SubscribeToPlanRequest
        {
            ShopperUserId = UserId,
            Email = "demouser@microsoft.com",
            ProductHandle = "eshop-pro"
        }, default);

        Assert.False(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<CreateMaxioCustomerRequest>(), Arg.Any<CancellationToken>());
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<CreateMaxioSubscriptionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_UnknownPlan_Throws()
    {
        _maxio.ListProductsForFamilyAsync(Family, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, ProductFamilyHandle = Family }
            });

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() =>
            CreateService().SubscribeAsync(new SubscribeToPlanRequest
            {
                ShopperUserId = UserId,
                Email = "demouser@microsoft.com",
                ProductHandle = "not-a-plan"
            }, default));
    }

    [Fact]
    public async Task ListMySubscriptionsAsync_ReturnsEmptyWhenCustomerDoesNotExist()
    {
        _maxio.GetCustomerByReferenceAsync(SubscriptionBillingService.CustomerReferenceFor(UserId), Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var subscriptions = await CreateService().ListMySubscriptionsAsync(UserId, default);

        Assert.Empty(subscriptions);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }
}
