using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class GetSubscriptions : SubscriptionServiceFixture
{
    [Fact]
    public async Task ReturnsNothingForAShopperWhoHasNeverSubscribed()
    {
        GivenExistingCustomer(null);

        var subscriptions = await CreateService().GetSubscriptionsAsync(Subscriber);

        Assert.Empty(subscriptions);
        await MockGateway.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadsTheShoppersSubscriptionsFromTheBillingSystem()
    {
        GivenExistingCustomer(Customer());
        GivenSubscriptions(Subscription(1, ProPlanHandle), Subscription(2, BasicPlanHandle));

        var subscriptions = await CreateService().GetSubscriptionsAsync(Subscriber);

        Assert.Equal(2, subscriptions.Count);
        await MockGateway.Received(1).ListCustomerSubscriptionsAsync(CustomerId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListsTheMostRecentlyActivatedSubscriptionFirst()
    {
        GivenExistingCustomer(Customer());
        GivenSubscriptions(
            WithActivation(Subscription(1, ProPlanHandle), DateTimeOffset.UtcNow.AddDays(-30)),
            WithActivation(Subscription(2, BasicPlanHandle), DateTimeOffset.UtcNow.AddDays(-1)));

        var subscriptions = await CreateService().GetSubscriptionsAsync(Subscriber);

        Assert.Equal(2, subscriptions.First().Id);
    }

    private static CustomerSubscription WithActivation(CustomerSubscription source, DateTimeOffset activatedAt) =>
        new(source.Id, source.Reference, source.State, source.PlanHandle, source.PlanName, source.PriceInCents,
            source.Interval, source.IntervalUnit, source.CurrentPeriodStartedAt, source.CurrentPeriodEndsAt,
            source.NextBillingAt, activatedAt, source.CanceledAt, source.PaymentCollectionMethod,
            source.CustomerId, source.CustomerReference);
}
