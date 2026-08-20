using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionBillingServiceTests
{
    [TestMethod]
    public async Task ConcurrentDoubleSubmitCreatesExactlyOneMaxioSubscription()
    {
        var maxio = new FakeMaxioClient();
        var store = new FakeLinkStore();
        var service = new SubscriptionBillingService(maxio, store, new MaxioOptions
        {
            ProductFamilyHandle = "family"
        });
        var user = new BillingUser("user-id", "buyer@example.com", "buyer@example.com");

        var results = await Task.WhenAll(
            service.SubscribeAsync(user, "pro", CancellationToken.None),
            service.SubscribeAsync(user, "pro", CancellationToken.None));

        Assert.AreEqual(1, maxio.CreateCustomerCount);
        Assert.AreEqual(1, maxio.CreateSubscriptionCount);
        Assert.AreEqual(1, results.Count(result => result.Created));
        Assert.AreEqual(1, results.Count(result => !result.Created));
        Assert.IsNotNull(store.Link);
    }

    private sealed class FakeMaxioClient : IMaxioBillingClient
    {
        private MaxioCustomer? _customer;
        private MaxioSubscription? _subscription;
        public int CreateCustomerCount { get; private set; }
        public int CreateSubscriptionCount { get; private set; }

        public Task<IReadOnlyList<MaxioPlan>> GetProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
        {
            IReadOnlyList<MaxioPlan> plans = new[]
            {
                new MaxioPlan(1, "Pro", "pro", null, 29900, 1, "month", false)
            };
            return Task.FromResult(plans);
        }

        public Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MaxioSite(RelationshipInvoicingEnabled: true, IsTestSite: true));

        public Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken) =>
            Task.FromResult(_customer);

        public Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomer customer, CancellationToken cancellationToken)
        {
            CreateCustomerCount++;
            _customer = new MaxioCustomer(10, customer.Reference, customer.Email);
            return Task.FromResult(_customer);
        }

        public Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken) =>
            Task.FromResult(_subscription);

        public Task<MaxioSubscription> CreateSubscriptionAsync(CreateMaxioSubscription subscription, CancellationToken cancellationToken)
        {
            CreateSubscriptionCount++;
            _subscription = new MaxioSubscription(
                20,
                subscription.SubscriptionReference,
                "active",
                29900,
                DateTimeOffset.UtcNow.AddMonths(1),
                _customer!.Id,
                subscription.CustomerReference,
                1,
                "Pro",
                subscription.ProductHandle,
                1,
                "month");
            return Task.FromResult(_subscription);
        }

        public Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
        {
            IReadOnlyList<MaxioSubscription> subscriptions = _subscription is null
                ? Array.Empty<MaxioSubscription>()
                : new[] { _subscription };
            return Task.FromResult(subscriptions);
        }
    }

    private sealed class FakeLinkStore : ISubscriptionLinkStore
    {
        public SubscriptionLink? Link { get; private set; }

        public Task<SubscriptionLink?> FindAsync(string userId, string productHandle, CancellationToken cancellationToken) =>
            Task.FromResult(Link);

        public Task SaveAsync(SubscriptionLink link, CancellationToken cancellationToken)
        {
            Link = link;
            return Task.CompletedTask;
        }
    }
}
