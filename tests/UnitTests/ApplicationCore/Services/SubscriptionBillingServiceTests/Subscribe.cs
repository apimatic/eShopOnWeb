using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingServiceTests;

public class Subscribe
{
    private readonly IMaxioBillingClient _maxio = Substitute.For<IMaxioBillingClient>();
    private readonly IAppLogger<SubscriptionBillingService> _logger = Substitute.For<IAppLogger<SubscriptionBillingService>>();
    private readonly ShopperIdentity _shopper = new("demouser@microsoft.com", "demouser@microsoft.com", "demouser", "eShopOnWeb");
    private readonly SubscriptionPlan _proPlan = new()
    {
        Id = 1,
        Handle = "eshop-pro",
        Name = "Pro Plan",
        PriceInCents = 29900,
        Interval = 1,
        IntervalUnit = "month"
    };

    private SubscriptionBillingService CreateSut() => new(_maxio, _logger);

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenNoneExist()
    {
        _maxio.ListProductsForProductFamilyAsync(Arg.Any<CancellationToken>()).Returns(new[] { _proPlan });
        _maxio.ReadCustomerByReferenceAsync(_shopper.Reference, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);
        _maxio.CreateCustomerAsync(_shopper, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = _shopper.Reference, Email = _shopper.Email });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerSubscription>());
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((CustomerSubscription?)null);
        _maxio.CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new CustomerSubscription { Id = 99, State = "active", ProductHandle = "eshop-pro", ProductName = "Pro Plan", PriceInCents = 29900 });

        var result = await CreateSut().SubscribeAsync(_shopper, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        await _maxio.Received(1).CreateCustomerAsync(_shopper, Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(42, "eshop-pro", "demouser@microsoft.com:eshop-pro", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReusesExistingCustomerAndDoesNotCreateASecondSubscription()
    {
        var existing = new CustomerSubscription
        {
            Id = 7,
            State = "active",
            ProductHandle = "eshop-pro",
            ProductName = "Pro Plan",
            PriceInCents = 29900
        };

        _maxio.ListProductsForProductFamilyAsync(Arg.Any<CancellationToken>()).Returns(new[] { _proPlan });
        _maxio.ReadCustomerByReferenceAsync(_shopper.Reference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = _shopper.Reference });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>()).Returns(new[] { existing });

        var first = await CreateSut().SubscribeAsync(_shopper, "eshop-pro");
        var second = await CreateSut().SubscribeAsync(_shopper, "eshop-pro");

        Assert.False(first.Created);
        Assert.False(second.Created);
        Assert.Equal(7, second.Subscription.Id);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<ShopperIdentity>(), Arg.Any<CancellationToken>());
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoversWhenConcurrentCustomerCreateReturnsUnprocessableEntity()
    {
        _maxio.ListProductsForProductFamilyAsync(Arg.Any<CancellationToken>()).Returns(new[] { _proPlan });
        _maxio.ReadCustomerByReferenceAsync(_shopper.Reference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, new MaxioCustomer { Id = 42, Reference = _shopper.Reference });
        _maxio.CreateCustomerAsync(_shopper, Arg.Any<CancellationToken>())
            .Returns<MaxioCustomer>(_ => throw new MaxioApiException(HttpStatusCode.UnprocessableEntity, "reference taken"));
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CustomerSubscription>());
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((CustomerSubscription?)null);
        _maxio.CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new CustomerSubscription { Id = 99, State = "active", ProductHandle = "eshop-pro" });

        var result = await CreateSut().SubscribeAsync(_shopper, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(99, result.Subscription.Id);
    }

    [Fact]
    public async Task ThrowsWhenPlanHandleIsUnknown()
    {
        _maxio.ListProductsForProductFamilyAsync(Arg.Any<CancellationToken>()).Returns(new[] { _proPlan });

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => CreateSut().SubscribeAsync(_shopper, "not-a-plan"));
    }

    [Fact]
    public async Task ListMySubscriptionsReturnsEmptyWhenCustomerDoesNotExist()
    {
        _maxio.ReadCustomerByReferenceAsync(_shopper.Reference, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null);

        var result = await CreateSut().ListMySubscriptionsAsync(_shopper);

        Assert.Empty(result);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AllowsNewSubscribeAfterCanceledSubscription()
    {
        _maxio.ListProductsForProductFamilyAsync(Arg.Any<CancellationToken>()).Returns(new[] { _proPlan });
        _maxio.ReadCustomerByReferenceAsync(_shopper.Reference, Arg.Any<CancellationToken>())
            .Returns(new MaxioCustomer { Id = 42, Reference = _shopper.Reference });
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new CustomerSubscription { Id = 1, State = "canceled", ProductHandle = "eshop-pro" }
            });
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CustomerSubscription { Id = 1, State = "canceled", ProductHandle = "eshop-pro" });
        _maxio.CreateSubscriptionAsync(42, "eshop-pro", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new CustomerSubscription { Id = 2, State = "active", ProductHandle = "eshop-pro" });

        var result = await CreateSut().SubscribeAsync(_shopper, "eshop-pro");

        Assert.True(result.Created);
        Assert.Equal(2, result.Subscription.Id);
        await _maxio.Received(1).CreateSubscriptionAsync(42, "eshop-pro", null, Arg.Any<CancellationToken>());
    }
}
