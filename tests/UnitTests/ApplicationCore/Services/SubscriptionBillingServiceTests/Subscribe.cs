using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingServiceTests;

public class Subscribe
{
    private readonly IMaxioAdvancedBillingClient _maxio = Substitute.For<IMaxioAdvancedBillingClient>();
    private readonly IAppLogger<SubscriptionBillingService> _logger = Substitute.For<IAppLogger<SubscriptionBillingService>>();
    private readonly MaxioOptions _options = new()
    {
        ApiKey = "test-key",
        Subdomain = "test-site",
        ProductFamilyHandle = "eshop-subscribe"
    };
    private readonly ShopperIdentity _shopper = new("user-1", "demouser@microsoft.com", "demouser@microsoft.com");

    private SubscriptionBillingService CreateSut() => new(_maxio, _options, _logger);

    private static SubscriptionPlan Pro() => new()
    {
        Handle = "eshop-pro",
        Name = "Pro Plan",
        PriceInCents = 29900,
        Interval = 1,
        IntervalUnit = "month"
    };

    private static SubscriptionPlan Basic() => new()
    {
        Handle = "basic-plan",
        Name = "Basic Plan",
        PriceInCents = 2900,
        Interval = 1,
        IntervalUnit = "month"
    };

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenShopperIsNew()
    {
        _maxio.ListProductsInFamilyAsync("eshop-subscribe", default).Returns(new List<SubscriptionPlan> { Pro(), Basic() });
        _maxio.FindCustomerByReferenceAsync(Arg.Any<string>(), default).Returns((MaxioCustomerRecord?)null);
        _maxio.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), default)
            .Returns(new MaxioCustomerRecord { Id = 42, Email = _shopper.Email });
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), default).Returns((ShopperSubscription?)null);
        _maxio.ListCustomerSubscriptionsAsync(42, default).Returns(new List<ShopperSubscription>());
        _maxio.CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<string>(), default)
            .Returns(new ShopperSubscription
            {
                Id = 99,
                State = "active",
                ProductHandle = "eshop-pro",
                ProductName = "Pro Plan",
                PriceInCents = 29900
            });

        var result = await CreateSut().SubscribeAsync(_shopper, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal("eshop-pro", result.Subscription.ProductHandle);
        await _maxio.Received(1).CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), default);
        await _maxio.Received(1).CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<string>(), default);
    }

    [Fact]
    public async Task DoesNotCreateSecondSubscriptionOnDoubleClick()
    {
        var existing = new ShopperSubscription
        {
            Id = 99,
            State = "active",
            ProductHandle = "eshop-pro",
            ProductName = "Pro Plan",
            PriceInCents = 29900
        };

        _maxio.ListProductsInFamilyAsync("eshop-subscribe", default).Returns(new List<SubscriptionPlan> { Pro() });
        _maxio.FindCustomerByReferenceAsync(Arg.Any<string>(), default)
            .Returns(new MaxioCustomerRecord { Id = 42 });
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), default).Returns(existing);

        var result = await CreateSut().SubscribeAsync(_shopper, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), default);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), default);
    }

    [Fact]
    public async Task DefaultsToHighestPricedPlanWhenHandleOmitted()
    {
        _maxio.ListProductsInFamilyAsync("eshop-subscribe", default).Returns(new List<SubscriptionPlan> { Basic(), Pro() });
        _maxio.FindCustomerByReferenceAsync(Arg.Any<string>(), default)
            .Returns(new MaxioCustomerRecord { Id = 42 });
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), default).Returns((ShopperSubscription?)null);
        _maxio.ListCustomerSubscriptionsAsync(42, default).Returns(new List<ShopperSubscription>());
        _maxio.CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<string>(), default)
            .Returns(new ShopperSubscription { Id = 7, State = "active", ProductHandle = "eshop-pro", PriceInCents = 29900 });

        var result = await CreateSut().SubscribeAsync(_shopper, "");

        Assert.True(result.Created);
        await _maxio.Received(1).CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<string>(), default);
    }

    [Fact]
    public async Task RejectsUnknownPlanHandle()
    {
        _maxio.ListProductsInFamilyAsync("eshop-subscribe", default).Returns(new List<SubscriptionPlan> { Pro() });

        var ex = await Assert.ThrowsAsync<BillingException>(() => CreateSut().SubscribeAsync(_shopper, "not-a-plan"));

        Assert.Equal(400, ex.StatusCode);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), default);
    }

    [Fact]
    public async Task ListMineReturnsEmptyWhenCustomerDoesNotExist()
    {
        _maxio.FindCustomerByReferenceAsync(Arg.Any<string>(), default).Returns((MaxioCustomerRecord?)null);

        var result = await CreateSut().ListMySubscriptionsAsync(_shopper);

        Assert.Empty(result);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), default);
    }

    [Fact]
    public async Task ListPlansOrdersByPrice()
    {
        _maxio.ListProductsInFamilyAsync("eshop-subscribe", default).Returns(new List<SubscriptionPlan> { Pro(), Basic() });

        var plans = await CreateSut().ListPlansAsync();

        Assert.Equal(new[] { "basic-plan", "eshop-pro" }, plans.Select(p => p.Handle).ToArray());
    }
}
