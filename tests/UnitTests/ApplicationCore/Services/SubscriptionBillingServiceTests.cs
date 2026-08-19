using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class SubscriptionBillingServiceTests
{
    private readonly IMaxioAdvancedBillingClient _maxio = Substitute.For<IMaxioAdvancedBillingClient>();
    private readonly IAppLogger<SubscriptionBillingService> _logger = Substitute.For<IAppLogger<SubscriptionBillingService>>();
    private readonly MaxioSettings _settings = new()
    {
        ApiKey = "test-key",
        Subdomain = "test-site",
        ProductFamilyHandle = "eshop-subscribe"
    };

    private SubscriptionBillingService CreateService() =>
        new(_maxio, Options.Create(_settings), _logger);

    private static ShopperIdentity DemoShopper() => new()
    {
        BuyerId = "buyer-1",
        Email = "demouser@microsoft.com",
        UserName = "demouser@microsoft.com"
    };

    [Fact]
    public async Task ListPlansAsync_ReturnsProductsForConfiguredFamily()
    {
        var plans = new List<SubscriptionPlan>
        {
            new() { Handle = "eshop-pro", Name = "Pro Plan", ProductFamilyHandle = "eshop-subscribe" }
        };
        _maxio.ListProductsForProductFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(plans);

        var result = await CreateService().ListPlansAsync();

        Assert.Single(result);
        Assert.Equal("eshop-pro", result[0].Handle);
        await _maxio.Received(1).ListProductsForProductFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscription()
    {
        var shopper = DemoShopper();
        _maxio.ReadProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>())
            .Returns(new SubscriptionPlan { Handle = "eshop-pro", ProductFamilyHandle = "eshop-subscribe" });
        _maxio.ReadCustomerByReferenceAsync("eshop:buyer-1", Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);
        _maxio.CreateCustomerAsync(Arg.Any<BillingCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Email = shopper.Email, Reference = "eshop:buyer-1" });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ShopperSubscription>());
        _maxio.CreateSubscriptionAsync("eshop-pro", 42, "eshop:buyer-1:eshop-pro", Arg.Any<CancellationToken>())
            .Returns(new ShopperSubscription { Id = 99, State = "active", ProductHandle = "eshop-pro", ProductPriceInCents = 29900 });

        var result = await CreateService().SubscribeAsync(shopper, "eshop-pro");

        Assert.Equal(99, result.Id);
        Assert.Equal("active", result.State);
        await _maxio.Received(1).CreateCustomerAsync(
            Arg.Is<BillingCustomer>(c => c.Reference == "eshop:buyer-1" && c.Email == shopper.Email),
            Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync("eshop-pro", 42, "eshop:buyer-1:eshop-pro", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_IsIdempotentWhenLiveSubscriptionExists()
    {
        var shopper = DemoShopper();
        var existing = new ShopperSubscription { Id = 7, State = "active", ProductHandle = "eshop-pro" };
        _maxio.ReadProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>())
            .Returns(new SubscriptionPlan { Handle = "eshop-pro", ProductFamilyHandle = "eshop-subscribe" });
        _maxio.ReadCustomerByReferenceAsync("eshop:buyer-1", Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = "eshop:buyer-1" });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new[] { existing });

        var first = await CreateService().SubscribeAsync(shopper, "eshop-pro");
        var second = await CreateService().SubscribeAsync(shopper, "eshop-pro");

        Assert.Equal(7, first.Id);
        Assert.Equal(7, second.Id);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubscribeAsync_ThrowsNotFoundForUnknownPlan()
    {
        _maxio.ReadProductByHandleAsync("missing", Arg.Any<CancellationToken>())
            .Returns((SubscriptionPlan?)null);

        var ex = await Assert.ThrowsAsync<BillingException>(
            () => CreateService().SubscribeAsync(DemoShopper(), "missing"));

        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task ListMySubscriptionsAsync_ReturnsEmptyWhenCustomerDoesNotExist()
    {
        _maxio.ReadCustomerByReferenceAsync("eshop:buyer-1", Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);
        _maxio.ListCustomersAsync("demouser@microsoft.com", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BillingCustomer>());

        var result = await CreateService().ListMySubscriptionsAsync(DemoShopper());

        Assert.Empty(result);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
