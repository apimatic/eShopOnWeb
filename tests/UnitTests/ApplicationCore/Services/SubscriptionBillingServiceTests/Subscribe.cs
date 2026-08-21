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
    private readonly ShopperIdentity _shopper = new("user-1", "demo.user@example.com", "demo.user@example.com");

    public Subscribe()
    {
        _maxio.ProductFamilyHandle.Returns("eshop-subscribe");
        _maxio.GetProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>())
            .Returns(new BillingProduct
            {
                Id = 1,
                Handle = "eshop-pro",
                Name = "Pro Plan",
                PriceInCents = 29900,
                Interval = 1,
                IntervalUnit = "month",
                ProductFamilyHandle = "eshop-subscribe"
            });
    }

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenNoneExist()
    {
        _maxio.FindCustomerByReferenceAsync(_shopper.CustomerReference, Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);
        _maxio.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = _shopper.CustomerReference });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BillingSubscription>());
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BillingSubscription?)null);
        _maxio.CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BillingSubscription
            {
                Id = 99,
                State = "active",
                ProductHandle = "eshop-pro",
                ProductName = "Pro Plan",
                ProductPriceInCents = 29900
            });

        var service = new SubscriptionBillingService(_maxio, _logger);
        var result = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        await _maxio.Received(1).CreateCustomerAsync("Demo", "User", "demo.user@example.com", _shopper.CustomerReference, Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(42, "eshop-pro", _shopper.SubscriptionReference("eshop-pro"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsExistingLiveSubscriptionWithoutCreatingAnother()
    {
        _maxio.FindCustomerByReferenceAsync(_shopper.CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = _shopper.CustomerReference });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new BillingSubscription
                {
                    Id = 99,
                    State = "active",
                    ProductHandle = "eshop-pro",
                    ProductName = "Pro Plan",
                    ProductPriceInCents = 29900
                }
            });

        var service = new SubscriptionBillingService(_maxio, _logger);
        var result = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.False(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoversFromDuplicateCustomerReference()
    {
        _maxio.FindCustomerByReferenceAsync(_shopper.CustomerReference, Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null, new BillingCustomer { Id = 42, Reference = _shopper.CustomerReference });
        _maxio.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<BillingCustomer>>(_ => throw new BillingException(422, "reference must be unique"));
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BillingSubscription>());
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BillingSubscription?)null);
        _maxio.CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BillingSubscription { Id = 7, State = "active", ProductHandle = "eshop-pro" });

        var service = new SubscriptionBillingService(_maxio, _logger);
        var result = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(7, result.Subscription.Id);
    }

    [Fact]
    public async Task RejectsProductOutsideConfiguredFamily()
    {
        _maxio.GetProductByHandleAsync("other-plan", Arg.Any<CancellationToken>())
            .Returns(new BillingProduct
            {
                Handle = "other-plan",
                ProductFamilyHandle = "someone-else"
            });

        var service = new SubscriptionBillingService(_maxio, _logger);
        var ex = await Assert.ThrowsAsync<BillingException>(() => service.SubscribeAsync(_shopper, "other-plan"));
        Assert.Equal(400, ex.StatusCode);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AllowsResubscribeAfterCanceledEnrollment()
    {
        _maxio.FindCustomerByReferenceAsync(_shopper.CustomerReference, Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer { Id = 42, Reference = _shopper.CustomerReference });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new BillingSubscription
                {
                    Id = 50,
                    State = "canceled",
                    ProductHandle = "eshop-pro",
                    Reference = _shopper.SubscriptionReference("eshop-pro")
                }
            });
        _maxio.FindSubscriptionByReferenceAsync(_shopper.SubscriptionReference("eshop-pro"), Arg.Any<CancellationToken>())
            .Returns(new BillingSubscription { Id = 50, State = "canceled", ProductHandle = "eshop-pro" });
        _maxio.CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BillingSubscription { Id = 51, State = "active", ProductHandle = "eshop-pro" });

        var service = new SubscriptionBillingService(_maxio, _logger);
        var result = await service.SubscribeAsync(_shopper, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(51, result.Subscription.Id);
        await _maxio.Received(1).CreateSubscriptionAsync(
            42,
            "eshop-pro",
            Arg.Is<string>(r => r != _shopper.SubscriptionReference("eshop-pro")),
            Arg.Any<CancellationToken>());
    }
}
