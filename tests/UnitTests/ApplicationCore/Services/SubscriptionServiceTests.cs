using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class SubscriptionServiceTests
{
    private static readonly BillingUser User = new("user-123", "shopper@example.com", "Shopper", "Test");
    private static readonly SubscriptionPlan Plan = new(7, "pro", "Pro", "Pro plan", 29900, 1, "month");
    private static readonly BillingCustomer Customer = new(11, "customer-reference");
    private static readonly SubscriptionDetails Subscription = new(
        13, 11, "subscription-reference", "family", "pro", "Pro", 29900, 1, "month", "active",
        DateTimeOffset.Parse("2030-02-01T00:00:00Z"));

    private readonly IBillingGateway _gateway = Substitute.For<IBillingGateway>();
    private readonly ISubscriptionRecordStore _store = Substitute.For<ISubscriptionRecordStore>();

    [Fact]
    public async Task ReturnsExistingSubscriptionWithoutCreatingAnything()
    {
        _gateway.ListPlansAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { Plan });
        _gateway.FindSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Subscription);

        var service = new SubscriptionService(_gateway, _store);
        var result = await service.SubscribeAsync(User, "PRO");

        Assert.False(result.Created);
        Assert.Equal(Subscription, result.Subscription);
        await _gateway.DidNotReceive().CreateCustomerAsync(
            Arg.Any<BillingUser>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _gateway.DidNotReceive().CreateSubscriptionAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _store.Received(1).SynchronizeAsync(User.Id, Subscription, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWithOpaqueDeterministicReferences()
    {
        string? customerReference = null;
        string? subscriptionReference = null;
        _gateway.ListPlansAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { Plan });
        _gateway.FindSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((SubscriptionDetails?)null);
        _gateway.FindCustomerAsync(Arg.Do<string>(value => customerReference = value), Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);
        _gateway.CreateCustomerAsync(User, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new BillingCustomer(Customer.Id, call.ArgAt<string>(1)));
        _gateway.CreateSubscriptionAsync(
                Arg.Any<string>(),
                Plan.Handle,
                Arg.Do<string>(value => subscriptionReference = value),
                Arg.Any<CancellationToken>())
            .Returns(Subscription);

        var service = new SubscriptionService(_gateway, _store);
        var result = await service.SubscribeAsync(User, Plan.Handle);

        Assert.True(result.Created);
        Assert.NotNull(customerReference);
        Assert.NotNull(subscriptionReference);
        Assert.DoesNotContain(User.Id, customerReference!);
        Assert.DoesNotContain(User.Id, subscriptionReference!);
        await _store.Received(1).SynchronizeAsync(User.Id, Subscription, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsProductOutsideConfiguredFamily()
    {
        _gateway.ListPlansAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { Plan });
        var service = new SubscriptionService(_gateway, _store);

        await Assert.ThrowsAsync<BillingProviderValidationException>(
            () => service.SubscribeAsync(User, "not-in-family"));

        await _gateway.DidNotReceive().FindCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListsLiveProviderSubscriptionsAndSynchronizesMappings()
    {
        _gateway.FindCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Customer);
        _gateway.ListCustomerSubscriptionsAsync(Customer.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription });
        var service = new SubscriptionService(_gateway, _store);

        var result = await service.ListForUserAsync(User);

        Assert.Single(result);
        Assert.Equal(Subscription, result[0]);
        await _store.Received(1).SynchronizeAsync(User.Id, Subscription, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SerializesConcurrentEnrollmentForTheSameUserAndPlan()
    {
        SubscriptionDetails? currentSubscription = null;
        _gateway.ListPlansAsync(Arg.Any<CancellationToken>()).Returns(new[] { Plan });
        _gateway.FindSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => currentSubscription);
        _gateway.FindCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Customer);
        _gateway.CreateSubscriptionAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await Task.Delay(50);
                currentSubscription = Subscription;
                return Subscription;
            });
        var service = new SubscriptionService(_gateway, _store);

        var results = await Task.WhenAll(
            service.SubscribeAsync(User, Plan.Handle),
            service.SubscribeAsync(User, Plan.Handle));

        Assert.Single(results, result => result.Created);
        Assert.Single(results, result => !result.Created);
        await _gateway.Received(1).CreateSubscriptionAsync(
            Arg.Any<string>(), Plan.Handle, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
