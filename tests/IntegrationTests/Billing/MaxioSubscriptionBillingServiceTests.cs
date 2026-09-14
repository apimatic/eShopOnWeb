#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    [Fact]
    public async Task ConcurrentDoubleClickCreatesOneCustomerAndSubscription()
    {
        var root = new InMemoryDatabaseRoot();
        var databaseName = Guid.NewGuid().ToString();
        var contextOptions = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(databaseName, root)
            .Options;
        await using var firstContext = new CatalogContext(contextOptions);
        await using var secondContext = new CatalogContext(contextOptions);
        var maxio = new FakeMaxioClient();
        var operationLock = new SubscriptionOperationLock();
        var options = Options.Create(new MaxioOptions { ProductFamilyHandle = "family" });
        var firstService = new MaxioSubscriptionBillingService(maxio, firstContext, operationLock, options);
        var secondService = new MaxioSubscriptionBillingService(maxio, secondContext, operationLock, options);
        var user = new SubscriptionUser("user-1", "shopper@example.com", "Shopper", "Example");

        var results = await Task.WhenAll(
            firstService.SubscribeAsync(user, "pro"),
            secondService.SubscribeAsync(user, "pro"));

        Assert.Equal(results[0].Id, results[1].Id);
        Assert.Equal(1, maxio.CustomerCreateCount);
        Assert.Equal(1, maxio.SubscriptionCreateCount);
        await using var verificationContext = new CatalogContext(contextOptions);
        var enrollment = Assert.Single(await verificationContext.SubscriptionEnrollments.AsNoTracking().ToListAsync());
        Assert.Equal(results[0].Id, enrollment.MaxioSubscriptionId);
    }

    private sealed class FakeMaxioClient : IMaxioClient
    {
        private readonly object _sync = new();
        private MaxioCustomer? _customer;
        private MaxioSubscription? _subscription;
        private int _customerCreateCount;
        private int _subscriptionCreateCount;

        public int CustomerCreateCount => _customerCreateCount;
        public int SubscriptionCreateCount => _subscriptionCreateCount;

        public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<SubscriptionPlan> plans = new[]
            {
                new SubscriptionPlan("pro", "Pro", "", 29900, 1, "month")
            };
            return Task.FromResult(plans);
        }

        public Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                return Task.FromResult(_customer);
            }
        }

        public Task<MaxioCustomer> CreateCustomerAsync(SubscriptionUser user, string reference, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                Interlocked.Increment(ref _customerCreateCount);
                _customer = new MaxioCustomer(7, reference);
                return Task.FromResult(_customer);
            }
        }

        public Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                return Task.FromResult(_subscription);
            }
        }

        public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                IReadOnlyList<MaxioSubscription> subscriptions = _subscription is null
                    ? Array.Empty<MaxioSubscription>()
                    : new[] { _subscription };
                return Task.FromResult(subscriptions);
            }
        }

        public async Task<MaxioSubscription> CreateSubscriptionAsync(
            string customerReference,
            string productHandle,
            string reference,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _subscriptionCreateCount);
            await Task.Delay(25, cancellationToken);
            lock (_sync)
            {
                _subscription = new MaxioSubscription(
                    new SubscriptionDetails(42, productHandle, "Pro", 29900, "USD", "active", DateTimeOffset.UtcNow.AddMonths(1)),
                    7,
                    customerReference,
                    "family",
                    reference);
                return _subscription;
            }
        }
    }
}
