using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public sealed class SubscriptionBillingServiceTests
{
    [TestMethod]
    public async Task ConcurrentDoubleClickCreatesOneCustomerAndOneSubscription()
    {
        var root = new InMemoryDatabaseRoot();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(databaseName, root)
            .Options;
        var gateway = new ConcurrentGateway();
        await using var db1 = new CatalogContext(options);
        await using var db2 = new CatalogContext(options);
        var service1 = new SubscriptionBillingService(db1, gateway, NullLogger<SubscriptionBillingService>.Instance);
        var service2 = new SubscriptionBillingService(db2, gateway, NullLogger<SubscriptionBillingService>.Instance);
        var user = new BillingUser("user-42", "shopper@example.com", "Shopper", "Example");

        var results = await Task.WhenAll(
            service1.SubscribeAsync(user, "eshop-pro", CancellationToken.None),
            service2.SubscribeAsync(user, "eshop-pro", CancellationToken.None));

        Assert.AreEqual(1, gateway.CreateCustomerCalls);
        Assert.AreEqual(1, gateway.CreateSubscriptionCalls);
        Assert.AreEqual(results[0], results[1]);
        await using var verification = new CatalogContext(options);
        Assert.AreEqual(1, await verification.MaxioCustomerLinks.CountAsync());
        Assert.AreEqual(1, await verification.MaxioSubscriptionEnrollments.CountAsync());
    }

    private sealed class ConcurrentGateway : IMaxioBillingGateway
    {
        private readonly ConcurrentDictionary<string, MaxioCustomer> _customers = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, SubscriptionDto> _subscriptions = new(StringComparer.Ordinal);
        private int _createCustomerCalls;
        private int _createSubscriptionCalls;

        public int CreateCustomerCalls => _createCustomerCalls;
        public int CreateSubscriptionCalls => _createSubscriptionCalls;

        public Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubscriptionPlanDto>>(
                new[] { new SubscriptionPlanDto("eshop-pro", "Pro", null, 29900, 1, "month", false) });

        public Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
        {
            _customers.TryGetValue(reference, out var customer);
            return Task.FromResult(customer);
        }

        public async Task<MaxioCustomer> CreateCustomerAsync(BillingUser user, string reference, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _createCustomerCalls);
            await Task.Delay(75, cancellationToken);
            var customer = new MaxioCustomer(101, reference);
            _customers[reference] = customer;
            return customer;
        }

        public Task<SubscriptionDto?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
        {
            _subscriptions.TryGetValue(reference, out var subscription);
            return Task.FromResult(subscription);
        }

        public async Task<SubscriptionDto> CreateSubscriptionAsync(
            string customerReference,
            string productHandle,
            string reference,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _createSubscriptionCalls);
            await Task.Delay(75, cancellationToken);
            var subscription = new SubscriptionDto(
                202,
                reference,
                productHandle,
                "Pro",
                29900,
                "active",
                DateTimeOffset.Parse("2026-09-25T00:00:00Z"),
                DateTimeOffset.Parse("2026-09-25T00:00:00Z"));
            _subscriptions[reference] = subscription;
            return subscription;
        }

        public Task<IReadOnlyList<SubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubscriptionDto>>(_subscriptions.Values.ToList());
    }
}
