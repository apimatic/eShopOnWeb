using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class SubscribeAsyncConcurrency : SubscriptionServiceFixture
{
    /// <summary>
    /// The shape of a shopper double-clicking Subscribe: several requests arrive at once and only
    /// one of them may reach the provider with a create.
    /// </summary>
    [Fact]
    public async Task EnrollsOnlyOnceWhenManyRequestsArriveTogether()
    {
        const int callers = 12;
        var created = new List<CustomerSubscription>();

        GivenPlanExists();
        GivenCustomerExists();

        Gateway.ListSubscriptionsAsync(CustomerId, Arg.Any<CancellationToken>())
            .Returns(_ => new List<CustomerSubscription>(created));

        Gateway.CreateSubscriptionAsync(Arg.Any<NewSubscription>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var subscription = Subscription(id: 500 + created.Count);
                created.Add(subscription);
                return subscription;
            });

        var service = CreateService();

        var results = await Task.WhenAll(Enumerable.Range(0, callers).Select(_ =>
            Task.Run(() => service.SubscribeAsync(new SubscribeRequest(Subscriber, PlanHandle)))));

        Assert.Single(created);
        Assert.Equal(1, results.Count(result => !result.AlreadySubscribed));
        Assert.Equal(callers - 1, results.Count(result => result.AlreadySubscribed));
        Assert.All(results, result => Assert.Equal(created[0].Id, result.Subscription.Id));
    }
}
