using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingServiceTests;

public class Subscribe
{
    private static readonly BillingUser User = new("user-42", "shopper@example.test", "Shopper", "Customer");
    private static readonly SubscriptionPlan Plan = new("eshop-pro", "Pro", 29900, 1, "month");
    private static readonly BillingCustomer Customer = new(17, "customer-ref");
    private static readonly BillingSubscription Subscription = new(
        29,
        "subscription-ref",
        "eshop-pro",
        "Pro",
        29900,
        1,
        "month",
        "active",
        DateTimeOffset.UtcNow.AddMonths(1));

    [Fact]
    public async Task ConcurrentDoubleClickCreatesAtMostOneProviderSubscription()
    {
        var gateway = Substitute.For<ISubscriptionBillingGateway>();
        var store = Substitute.For<ISubscriptionReservationStore>();
        var reservation = new SubscriptionReservation("user-42", "eshop-pro", "customer-ref", "subscription-ref");

        gateway.ListPlansAsync(Arg.Any<CancellationToken>()).Returns([Plan]);
        gateway.EnsureCustomerAsync(User, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Customer);
        gateway.FindSubscriptionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BillingSubscription?)null);
        gateway.CreateSubscriptionAsync("eshop-pro", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Subscription);
        gateway.ReadSubscriptionAsync(Subscription.Id, Arg.Any<CancellationToken>()).Returns(Subscription);
        store.GetOrCreateAsync(
                User.Id,
                "eshop-pro",
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((reservation, true)));

        var service = new SubscriptionBillingService(gateway, store);

        var results = await Task.WhenAll(
            service.SubscribeAsync(User, "eshop-pro", CancellationToken.None),
            service.SubscribeAsync(User, "eshop-pro", CancellationToken.None));

        Assert.All(results, result => Assert.Equal(Subscription.Id, result.Id));
        await gateway.Received(1).CreateSubscriptionAsync(
            "eshop-pro",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AccountReadDoesNotCreateMissingCustomer()
    {
        var gateway = Substitute.For<ISubscriptionBillingGateway>();
        var store = Substitute.For<ISubscriptionReservationStore>();
        gateway.FindCustomerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);
        var service = new SubscriptionBillingService(gateway, store);

        var subscriptions = await service.ListMySubscriptionsAsync(User, CancellationToken.None);

        Assert.Empty(subscriptions);
        await gateway.DidNotReceiveWithAnyArgs().EnsureCustomerAsync(default!, default!, default);
    }
}
