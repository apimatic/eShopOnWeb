using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Subscriptions;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionBillingServiceTests
{
    [TestMethod]
    public async Task ConcurrentDoubleClickCreatesOneCustomerAndOneSubscription()
    {
        var maxio = new FakeMaxioClient();
        await using var context = NewContext();
        var service = new SubscriptionBillingService(maxio, context, new SubscriptionCreationCoordinator());
        var shopper = new SubscriptionShopper("user-123", "shopper@example.com");

        var results = await Task.WhenAll(
            service.SubscribeAsync(shopper, "pro", CancellationToken.None),
            service.SubscribeAsync(shopper, "pro", CancellationToken.None));

        Assert.AreEqual(1, maxio.CustomerCreates);
        Assert.AreEqual(1, maxio.SubscriptionCreates);
        Assert.AreEqual(1, results.Count(result => result.Created));
        Assert.AreEqual(1, results.Count(result => !result.Created));
        Assert.AreEqual(1, await context.SubscriptionRecords.CountAsync());
        Assert.AreEqual(101, (await context.SubscriptionRecords.SingleAsync()).MaxioSubscriptionId);
    }

    [TestMethod]
    public async Task MySubscriptionsAlwaysUsesCurrentMaxioState()
    {
        var maxio = new FakeMaxioClient();
        await using var context = NewContext();
        var service = new SubscriptionBillingService(maxio, context, new SubscriptionCreationCoordinator());
        var shopper = new SubscriptionShopper("user-123", "shopper@example.com");
        await service.SubscribeAsync(shopper, "pro", CancellationToken.None);
        maxio.CurrentState = "on_hold";

        var subscriptions = await service.GetSubscriptionsAsync(shopper, CancellationToken.None);

        Assert.AreEqual("on_hold", subscriptions.Single().State);
    }

    private static SubscriptionDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<SubscriptionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SubscriptionDbContext(options);
    }

    private sealed class FakeMaxioClient : IMaxioClient
    {
        private readonly ConcurrentDictionary<string, MaxioSubscription> _subscriptions = new();
        private MaxioCustomer? _customer;

        public int CustomerCreates { get; private set; }
        public int SubscriptionCreates { get; private set; }
        public string CurrentState { get; set; } = "active";

        public Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MaxioProduct>>(new[] { Product() });

        public Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken) =>
            Task.FromResult(_customer is not null && _customer.Reference == reference ? _customer : null);

        public Task<MaxioCustomer> CreateCustomerAsync(string email, string reference,
            CancellationToken cancellationToken)
        {
            CustomerCreates++;
            _customer = new MaxioCustomer { Id = 88, Email = email, Reference = reference };
            return Task.FromResult(_customer);
        }

        public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId,
            CancellationToken cancellationToken)
        {
            foreach (var subscription in _subscriptions.Values)
            {
                subscription.State = CurrentState;
            }

            return Task.FromResult<IReadOnlyList<MaxioSubscription>>(_subscriptions.Values.ToArray());
        }

        public Task<MaxioSubscription?> FindSubscriptionAsync(string reference,
            CancellationToken cancellationToken) =>
            Task.FromResult(_subscriptions.TryGetValue(reference, out var value) ? value : null);

        public Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle,
            string reference, CancellationToken cancellationToken)
        {
            SubscriptionCreates++;
            var subscription = new MaxioSubscription
            {
                Id = 101,
                State = CurrentState,
                ProductPriceInCents = 29900,
                NextAssessmentAt = new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero),
                Reference = reference,
                Currency = "USD",
                Customer = _customer!,
                Product = Product()
            };
            _subscriptions[reference] = subscription;
            return Task.FromResult(subscription);
        }

        private static MaxioProduct Product() => new()
        {
            Id = 7,
            Name = "Pro",
            Handle = "pro",
            PriceInCents = 29900,
            Interval = 1,
            IntervalUnit = "month"
        };
    }
}
