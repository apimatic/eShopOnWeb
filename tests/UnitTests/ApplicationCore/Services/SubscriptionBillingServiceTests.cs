using Microsoft.eShopWeb.ApplicationCore.Billing;
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
        _sut = new SubscriptionBillingService(_maxio, _logger);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerOnce_AndReturnsExistingLiveSubscription()
    {
        var shopper = new ShopperIdentity("user-1", "demouser@microsoft.com", "demouser@microsoft.com");
        _maxio.FindCustomerByReferenceAsync("user-1", Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null, new BillingCustomer(42, "user-1", shopper.Email));
        _maxio.CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(42, "user-1", shopper.Email));
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<ShopperSubscription>
            {
                new(9, "active", "eshop-pro", "Pro Plan", 299m, 29900, DateTimeOffset.UtcNow.AddMonths(1))
            });

        var first = await _sut.SubscribeAsync(shopper, "eshop-pro");
        var second = await _sut.SubscribeAsync(shopper, "eshop-pro");

        Assert.False(first.Created);
        Assert.False(second.Created);
        Assert.Equal(9, first.Subscription.Id);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateCustomerAsync(Arg.Any<NewBillingCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_CreatesSubscription_WhenCustomerHasNoLivePlan()
    {
        var shopper = new ShopperIdentity("user-2", "admin@microsoft.com", "admin@microsoft.com");
        _maxio.FindCustomerByReferenceAsync("user-2", Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(7, "user-2", shopper.Email));
        _maxio.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>())
            .Returns(new List<ShopperSubscription>());
        _maxio.CreateSubscriptionAsync(7, "basic-plan", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ShopperSubscription(11, "active", "basic-plan", "Basic Plan", 29m, 2900, DateTimeOffset.UtcNow.AddMonths(1)));

        var result = await _sut.SubscribeAsync(shopper, "basic-plan");

        Assert.True(result.Created);
        Assert.Equal("basic-plan", result.Subscription.ProductHandle);
        Assert.Equal("active", result.Subscription.State);
    }

    [Fact]
    public async Task ListMySubscriptions_ReturnsEmpty_WhenCustomerDoesNotExist()
    {
        _maxio.FindCustomerByReferenceAsync("missing", Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);

        var result = await _sut.ListMySubscriptionsAsync("missing");

        Assert.Empty(result);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
