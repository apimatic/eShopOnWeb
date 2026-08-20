using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class Subscribe
{
    private readonly IMaxioBillingClient _maxio = Substitute.For<IMaxioBillingClient>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();
    private readonly SubscriptionService _sut;

    public Subscribe()
    {
        _maxio.IsConfigured.Returns(true);
        _sut = new SubscriptionService(_maxio, new ImmediateSubscriptionLock(), _logger);
    }

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenShopperIsNew()
    {
        var shopper = NewShopper();
        var plan = ProPlan();
        var customer = new BillingCustomer { Id = 42, Reference = shopper.UserId, Email = shopper.Email };
        var subscription = LiveSubscription(99, plan.Handle);

        _maxio.ListPlansAsync(default).Returns(new List<BillingPlan> { plan });
        _maxio.FindCustomerByReferenceAsync(shopper.UserId, default).Returns((BillingCustomer?)null);
        _maxio.CreateCustomerAsync(shopper, Arg.Any<string>(), default).Returns(customer);
        _maxio.ListCustomerSubscriptionsAsync(customer.Id, default).Returns(new List<BillingSubscription>());
        _maxio.CreateSubscriptionAsync(customer.Id, plan.Handle, Arg.Any<string>(), false, default).Returns(subscription);

        var result = await _sut.SubscribeAsync(shopper, plan.Handle);

        Assert.True(result.Created);
        Assert.Equal(99, result.Subscription.Id);
        await _maxio.Received(1).CreateCustomerAsync(shopper, Arg.Any<string>(), default);
        await _maxio.Received(1).CreateSubscriptionAsync(customer.Id, plan.Handle, Arg.Any<string>(), false, default);
    }

    [Fact]
    public async Task ReturnsExistingLiveSubscriptionWithoutCreatingAnother()
    {
        var shopper = NewShopper();
        var plan = ProPlan();
        var customer = new BillingCustomer { Id = 7, Reference = shopper.UserId, Email = shopper.Email };
        var existing = LiveSubscription(15, plan.Handle);

        _maxio.ListPlansAsync(default).Returns(new List<BillingPlan> { plan });
        _maxio.FindCustomerByReferenceAsync(shopper.UserId, default).Returns(customer);
        _maxio.ListCustomerSubscriptionsAsync(customer.Id, default).Returns(new List<BillingSubscription> { existing });

        var first = await _sut.SubscribeAsync(shopper, plan.Handle);
        var second = await _sut.SubscribeAsync(shopper, plan.Handle);

        Assert.False(first.Created);
        Assert.False(second.Created);
        Assert.Equal(15, first.Subscription.Id);
        Assert.Equal(15, second.Subscription.Id);
        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<ShopperIdentity>(), Arg.Any<string>(), default);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), default);
    }

    [Fact]
    public async Task ThrowsWhenPlanIsNotInConfiguredFamily()
    {
        var shopper = NewShopper();
        _maxio.ListPlansAsync(default).Returns(new List<BillingPlan> { ProPlan() });

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => _sut.SubscribeAsync(shopper, "unknown-plan"));
    }

    [Fact]
    public async Task ListPlansExcludesArchivedProducts()
    {
        _maxio.ListPlansAsync(default).Returns(new List<BillingPlan>
        {
            ProPlan(),
            new BillingPlan { Handle = "old-plan", Name = "Old", ArchivedAt = System.DateTimeOffset.UtcNow }
        });

        var plans = await _sut.ListPlansAsync();

        Assert.Single(plans);
        Assert.Equal("eshop-pro", plans[0].Handle);
    }

    [Fact]
    public async Task ListMySubscriptionsReturnsEmptyWhenCustomerDoesNotExist()
    {
        var shopper = NewShopper();
        _maxio.FindCustomerByReferenceAsync(shopper.UserId, default).Returns((BillingCustomer?)null);

        var subscriptions = await _sut.ListMySubscriptionsAsync(shopper);

        Assert.Empty(subscriptions);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), default);
    }

    private static ShopperIdentity NewShopper() => new()
    {
        UserId = "user-1",
        Email = "demouser@microsoft.com",
        FirstName = "Demo",
        LastName = "User"
    };

    private static BillingPlan ProPlan() => new()
    {
        Handle = "eshop-pro",
        Name = "Pro Plan",
        PriceInCents = 29900,
        Interval = 1,
        IntervalUnit = "month"
    };

    private static BillingSubscription LiveSubscription(int id, string handle) => new()
    {
        Id = id,
        State = "active",
        ProductHandle = handle,
        ProductName = "Pro Plan",
        PriceInCents = 29900
    };

    private sealed class ImmediateSubscriptionLock : ISubscriptionIdempotencyLock
    {
        public Task<T> ExecuteAsync<T>(string key, System.Func<Task<T>> action, System.Threading.CancellationToken cancellationToken = default)
            => action();
    }
}
