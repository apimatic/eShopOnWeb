using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

/// <summary>
/// The double-click case: several simultaneous subscribe requests for the same shopper must leave
/// exactly one customer and one subscription behind.
/// </summary>
public class SubscribeConcurrently : SubscriptionServiceFixture
{
    private const int ConcurrentRequests = 8;

    [Fact]
    public async Task CreatesOneCustomerAndOneSubscriptionForSimultaneousRequests()
    {
        GivenPlans(Plan(ProPlanHandle));

        // A gateway that behaves like the real one: state written by a create is visible to reads
        // that follow it.
        BillingCustomer? customer = null;
        var subscriptions = new ConcurrentBag<CustomerSubscription>();
        var createdCustomers = 0;
        var createdSubscriptions = 0;

        MockGateway.FindCustomerByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => customer);
        MockGateway.CreateCustomerAsync(Arg.Any<NewCustomerRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref createdCustomers);
                customer = Customer();
                return customer;
            });
        MockGateway.ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => (IReadOnlyCollection<CustomerSubscription>)subscriptions.ToList());
        MockGateway.CreateSubscriptionAsync(Arg.Any<NewSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var id = Interlocked.Increment(ref createdSubscriptions);
                var subscription = Subscription(id, ProPlanHandle);
                subscriptions.Add(subscription);
                return subscription;
            });

        var service = CreateService();
        var results = await Task.WhenAll(Enumerable.Range(0, ConcurrentRequests)
            .Select(_ => Task.Run(() => service.SubscribeAsync(Subscriber, ProPlanHandle))));

        Assert.Equal(1, createdCustomers);
        Assert.Equal(1, createdSubscriptions);
        Assert.Single(results, r => r.Created);
        Assert.All(results, r => Assert.Equal(1, r.Subscription.Id));
    }

    [Fact]
    public async Task DoesNotSerialiseDifferentShoppers()
    {
        GivenPlans(Plan(ProPlanHandle));
        GivenExistingCustomer(Customer());
        GivenSubscriptions();

        var inFlight = 0;
        var peakInFlight = 0;
        MockGateway.CreateSubscriptionAsync(Arg.Any<NewSubscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                var current = Interlocked.Increment(ref inFlight);
                InterlockedMax(ref peakInFlight, current);
                await Task.Delay(50);
                Interlocked.Decrement(ref inFlight);
                return Subscription(1, ProPlanHandle);
            });

        var service = CreateService();
        var shoppers = Enumerable.Range(0, 4)
            .Select(i => new SubscriberIdentity($"shopper{i}@microsoft.com", $"shopper{i}@microsoft.com"));

        await Task.WhenAll(shoppers.Select(s => Task.Run(() => service.SubscribeAsync(s, ProPlanHandle))));

        Assert.True(peakInFlight > 1, $"Expected shoppers to subscribe in parallel but peak concurrency was {peakInFlight}.");
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value)
        {
            if (Interlocked.CompareExchange(ref target, value, current) == current) return;
        }
    }
}
