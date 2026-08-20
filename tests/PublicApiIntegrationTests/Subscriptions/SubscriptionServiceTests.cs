using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Subscriptions;

[TestClass]
public class SubscriptionServiceTests
{
    [TestMethod]
    public async Task ConcurrentSubscribeCreatesOneCustomerAndOneSubscription()
    {
        var maxio = new FakeMaxioClient();
        var mappings = new FakeMappingStore();
        var service = new SubscriptionService(maxio, mappings, new SubscriptionOperationLock());
        var user = new ApplicationUser
        {
            Id = "user-123",
            UserName = "shopper@example.com",
            Email = "shopper@example.com"
        };

        var results = await Task.WhenAll(
            service.SubscribeAsync(user, "eshop-pro", CancellationToken.None),
            service.SubscribeAsync(user, "eshop-pro", CancellationToken.None));

        Assert.AreEqual(1, maxio.CreateCustomerCalls);
        Assert.AreEqual(1, maxio.CreateSubscriptionCalls);
        Assert.AreEqual(1, results.Count(result => !result.AlreadySubscribed));
        Assert.AreEqual(1, results.Count(result => result.AlreadySubscribed));
        Assert.IsTrue(results.All(result => result.Subscription.Id == 99));
        Assert.AreEqual(2, mappings.SyncCalls);
    }

    [TestMethod]
    public async Task ListForUnknownUserReturnsEmptyWithoutCreatingCustomer()
    {
        var maxio = new FakeMaxioClient();
        var service = new SubscriptionService(maxio, new FakeMappingStore(),
            new SubscriptionOperationLock());
        var user = new ApplicationUser { Id = "unknown", UserName = "unknown@example.com" };

        var subscriptions = await service.ListForUserAsync(user, CancellationToken.None);

        Assert.AreEqual(0, subscriptions.Count);
        Assert.AreEqual(0, maxio.CreateCustomerCalls);
    }

    private sealed class FakeMaxioClient : IMaxioBillingClient
    {
        private readonly List<MaxioSubscription> _subscriptions = new();
        private MaxioCustomer? _customer;

        public int CreateCustomerCalls { get; private set; }
        public int CreateSubscriptionCalls { get; private set; }

        public Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MaxioProduct>>(new[]
            {
                new MaxioProduct
                {
                    Id = 1,
                    Handle = "eshop-pro",
                    Name = "Pro",
                    PriceInCents = 29900,
                    Interval = 1,
                    IntervalUnit = "month"
                }
            });

        public Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken) =>
            Task.FromResult(_customer);

        public Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName,
            string email, string uniquenessToken, CancellationToken cancellationToken)
        {
            CreateCustomerCalls++;
            _customer = new MaxioCustomer { Id = 10, Reference = reference };
            return Task.FromResult(_customer);
        }

        public Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(long customerId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MaxioSubscription>>(_subscriptions.ToList());

        public Task<MaxioSubscription> CreateSubscriptionAsync(string customerReference, string productHandle,
            string subscriptionReference, string uniquenessToken, CancellationToken cancellationToken)
        {
            CreateSubscriptionCalls++;
            var subscription = new MaxioSubscription
            {
                Id = 99,
                State = "active",
                ProductPriceInCents = 29900,
                Product = new MaxioProduct
                {
                    Handle = productHandle,
                    Name = "Pro",
                    Interval = 1,
                    IntervalUnit = "month"
                }
            };
            _subscriptions.Add(subscription);
            return Task.FromResult(subscription);
        }
    }

    private sealed class FakeMappingStore : ISubscriptionMappingStore
    {
        public int SyncCalls { get; private set; }

        public Task SyncAsync(string userId, MaxioCustomer customer,
            IReadOnlyList<MaxioSubscription> subscriptions, CancellationToken cancellationToken)
        {
            SyncCalls++;
            return Task.CompletedTask;
        }
    }
}
