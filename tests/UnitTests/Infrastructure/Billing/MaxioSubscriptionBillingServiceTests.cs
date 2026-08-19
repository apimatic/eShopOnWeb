using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private readonly IMaxioAdvancedBillingClient _maxio = Substitute.For<IMaxioAdvancedBillingClient>();
    private readonly MaxioOptions _options = new()
    {
        ApiKey = "test-key",
        Subdomain = "example",
        ProductFamilyHandle = "eshop-subscribe"
    };

    private MaxioSubscriptionBillingService CreateSut()
    {
        return new MaxioSubscriptionBillingService(_maxio, Options.Create(_options), NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    [Fact]
    public async Task ListPlansAsync_MapsPriceFromCents()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
            });

        var plans = await CreateSut().ListPlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal("month", plan.IntervalUnit);
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerOnceAndReturnsExistingLiveSubscription()
    {
        var shopper = new ShopperIdentity("user-1", "demouser@microsoft.com", "demouser@microsoft.com");
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
            });
        _maxio.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        _maxio.CreateCustomerAsync(Arg.Any<MaxioCustomerPayload>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = "user-1" });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>
            {
                new()
                {
                    Id = 99,
                    State = "active",
                    ProductPriceInCents = 29900,
                    Product = new MaxioProduct { Handle = "eshop-pro", Name = "Pro Plan" }
                }
            });

        var result = await CreateSut().SubscribeAsync(shopper, "eshop-pro");

        Assert.Equal(99, result.Id);
        Assert.Equal("active", result.State);
        Assert.Equal(299.00m, result.Price);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_CreatesSubscriptionWhenNoneExists()
    {
        var shopper = new ShopperIdentity("user-2", "admin@microsoft.com", "admin@microsoft.com");
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                new() { Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" }
            });
        _maxio.FindCustomerByReferenceAsync("user-2", Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 7, Reference = "user-2" });
        _maxio.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>());
        _maxio.CreateSubscriptionAsync(7, "basic-plan", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MaxioSubscription
            {
                Id = 1001,
                State = "active",
                ProductPriceInCents = 2900,
                Product = new MaxioProduct { Handle = "basic-plan", Name = "Basic Plan" }
            });

        var result = await CreateSut().SubscribeAsync(shopper, "basic-plan");

        Assert.Equal(1001, result.Id);
        Assert.Equal("basic-plan", result.ProductHandle);
        await _maxio.Received(1).CreateSubscriptionAsync(7, "basic-plan", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsWhenPlanIsNotInFamily()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" }
            });

        var shopper = new ShopperIdentity("user-3", "a@b.com", "a@b.com");

        await Assert.ThrowsAsync<PlanNotFoundException>(() =>
            CreateSut().SubscribeAsync(shopper, "unknown-plan"));
    }
}
