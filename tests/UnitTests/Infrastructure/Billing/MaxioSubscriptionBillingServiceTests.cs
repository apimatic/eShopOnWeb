using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private readonly IMaxioApiClient _maxio = Substitute.For<IMaxioApiClient>();
    private readonly MaxioOptions _options = new()
    {
        ApiKey = "test-key",
        Subdomain = "test-site",
        ProductFamilyHandle = "eshop-subscribe"
    };
    private readonly ShopperIdentity _shopper = new("user-1", "demouser@microsoft.com", "Demouser", "Shopper");

    private MaxioSubscriptionBillingService CreateService() =>
        new(_maxio, Options.Create(_options), Substitute.For<ILogger<MaxioSubscriptionBillingService>>());

    [Fact]
    public async Task ListPlansAsync_OmitsArchivedProductsAndMapsPrice()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", default).Returns(new List<MaxioProduct>
        {
            new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" },
            new() { Handle = "retired", Name = "Old", PriceInCents = 100, Interval = 1, IntervalUnit = "month", ArchivedAt = DateTimeOffset.UtcNow }
        });

        var plans = await CreateService().ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal(29900, plan.PriceInCents);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", default).Returns(new List<MaxioProduct>
        {
            new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
        });
        _maxio.GetCustomerByReferenceAsync("user-1", default).Returns((MaxioCustomer?)null);
        _maxio.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<string>(), default)
            .Returns(new MaxioCustomer { Id = 42, Reference = "user-1" });
        _maxio.ListCustomerSubscriptionsAsync(42, default).Returns(Array.Empty<MaxioSubscription>());
        _maxio.FindSubscriptionByReferenceAsync("user-1:eshop-pro", default).Returns((MaxioSubscription?)null);
        _maxio.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<string>(), default)
            .Returns(new MaxioSubscription
            {
                Id = 99,
                State = "active",
                ProductPriceInCents = 29900,
                CurrentPeriodEndsAt = new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero),
                Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900 }
            });

        var result = await CreateService().SubscribeAsync(_shopper, "eshop-pro");

        Assert.Equal(99, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.ProductHandle);
        Assert.Equal(299.00m, result.Price);
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero), result.NextBillingAt);
        await _maxio.Received(1).CreateCustomerAsync(
            Arg.Is<MaxioCreateCustomer>(c => c.Reference == "user-1" && c.Email == "demouser@microsoft.com"),
            Arg.Any<string>(),
            default);
        await _maxio.Received(1).CreateSubscriptionAsync(
            Arg.Is<MaxioCreateSubscription>(s =>
                s.CustomerId == 42
                && s.ProductHandle == "eshop-pro"
                && s.PaymentCollectionMethod == "remittance"),
            Arg.Any<string>(),
            default);
    }

    [Fact]
    public async Task SubscribeAsync_IsIdempotentWhenLiveSubscriptionExists()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", default).Returns(new List<MaxioProduct>
        {
            new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
        });
        _maxio.GetCustomerByReferenceAsync("user-1", default)
            .Returns(new MaxioCustomer { Id = 42, Reference = "user-1" });
        _maxio.ListCustomerSubscriptionsAsync(42, default).Returns(new List<MaxioSubscription>
        {
            new()
            {
                Id = 99,
                State = "active",
                ProductPriceInCents = 29900,
                Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan" }
            }
        });

        var service = CreateService();
        var first = await service.SubscribeAsync(_shopper, "eshop-pro");
        var second = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.Equal(99, first.Id);
        Assert.Equal(99, second.Id);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<string>(), default);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<string>(), default);
    }

    [Fact]
    public async Task SubscribeAsync_RejectsUnknownPlan()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", default).Returns(new List<MaxioProduct>
        {
            new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
        });

        var ex = await Assert.ThrowsAsync<BillingException>(() => CreateService().SubscribeAsync(_shopper, "not-a-plan"));

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task ListMySubscriptionsAsync_ReturnsEmptyWhenCustomerDoesNotExist()
    {
        _maxio.GetCustomerByReferenceAsync("user-1", default).Returns((MaxioCustomer?)null);

        var result = await CreateService().ListMySubscriptionsAsync("user-1");

        Assert.Empty(result);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), default);
    }
}
