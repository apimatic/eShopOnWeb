using System.Collections.Concurrent;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class Subscribe
{
    [Fact]
    public async Task ConcurrentRequestsCreateOneCustomerAndSubscription()
    {
        var gateway = new BillingGateway();
        var store = new RecordStore();
        var service = new SubscriptionService(gateway, store);
        var userId = Guid.NewGuid().ToString();

        var results = await Task.WhenAll(
            service.SubscribeAsync(userId, "user@example.com", "eshop-pro"),
            service.SubscribeAsync(userId, "user@example.com", "eshop-pro"));

        Assert.Equal(1, gateway.CustomerCreateCount);
        Assert.Equal(1, gateway.SubscriptionCreateCount);
        Assert.Single(results.Where(result => result.WasCreated));
        Assert.Single(results.Where(result => !result.WasCreated));
        Assert.All(results, result => Assert.Equal(42, result.Subscription.Id));
    }

    [Fact]
    public async Task RejectsProductOutsideConfiguredFamilyBeforePersisting()
    {
        var store = new RecordStore();
        var service = new SubscriptionService(new BillingGateway(), store);

        await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(() =>
            service.SubscribeAsync(Guid.NewGuid().ToString(), "user@example.com", "other-plan"));

        Assert.Equal(0, store.Count);
    }

    private sealed class RecordStore : ISubscriptionRecordStore
    {
        private readonly ConcurrentDictionary<string, SubscriptionRecord> _records = new();
        public int Count => _records.Count;

        public Task<SubscriptionRecord?> GetAsync(string userId, string productHandle, CancellationToken cancellationToken = default)
        {
            _records.TryGetValue(Key(userId, productHandle), out var record);
            return Task.FromResult(record);
        }

        public Task<IReadOnlyList<SubscriptionRecord>> ListAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionRecord>>(_records.Values.Where(record => record.UserId == userId).ToArray());

        public Task<bool> TryAddAsync(SubscriptionRecord record, CancellationToken cancellationToken = default) =>
            Task.FromResult(_records.TryAdd(Key(record.UserId, record.ProductHandle), record));

        public Task SaveAsync(SubscriptionRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;

        private static string Key(string userId, string productHandle) => userId + "\n" + productHandle;
    }

    private sealed class BillingGateway : IMaxioBillingGateway
    {
        private BillingCustomer? _customer;
        private BillingSubscription? _subscription;
        public int CustomerCreateCount;
        public int SubscriptionCreateCount;

        public Task<IReadOnlyList<BillingPlan>> GetPlansAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BillingPlan>>(new[]
            {
                new BillingPlan(2, "eshop-pro", "Pro", null, 29900, 1, "month", false)
            });

        public Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken = default) =>
            Task.FromResult(_customer);

        public Task<BillingCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref CustomerCreateCount);
            _customer = new BillingCustomer(7, reference, email);
            return Task.FromResult(_customer);
        }

        public Task<BillingSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken = default) =>
            Task.FromResult(_subscription);

        public async Task<BillingSubscription> CreateSubscriptionAsync(long customerId, string productHandle, string reference, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref SubscriptionCreateCount);
            await Task.Delay(25, cancellationToken);
            _subscription = new BillingSubscription(42, reference, productHandle, "Pro", 29900, 1, "month", "active", DateTimeOffset.UtcNow.AddMonths(1), customerId, "family-under-test");
            return _subscription;
        }

        public Task<IReadOnlyList<BillingSubscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BillingSubscription>>(_subscription is null ? Array.Empty<BillingSubscription>() : new[] { _subscription });
    }
}
