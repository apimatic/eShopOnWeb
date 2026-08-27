using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace PublicApiIntegrationTests;

public sealed class FakeMaxioSubscriptionGateway : IMaxioSubscriptionGateway
{
    private readonly ConcurrentDictionary<string, MaxioCustomer> _customers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, StoredSubscription> _subscriptions = new(StringComparer.Ordinal);
    private int _nextCustomerId = 100;
    private int _nextSubscriptionId = 1000;
    private int _customerCreateCalls;
    private int _subscriptionCreateCalls;

    public int CustomerCreateCalls => Volatile.Read(ref _customerCreateCalls);
    public int SubscriptionCreateCalls => Volatile.Read(ref _subscriptionCreateCalls);

    public Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MaxioPlan>>(new[]
        {
            new MaxioPlan("basic-plan", "Basic Plan", "Basic", 2900, 1, "month", false),
            new MaxioPlan("eshop-pro", "Pro Plan", "Pro", 29900, 1, "month", false),
        });

    public Task<MaxioCustomer> EnsureCustomerAsync(
        string reference,
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken)
    {
        var customer = _customers.GetOrAdd(reference, key =>
        {
            Interlocked.Increment(ref _customerCreateCalls);
            return new MaxioCustomer(Interlocked.Increment(ref _nextCustomerId), key);
        });
        return Task.FromResult(customer);
    }

    public Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken) =>
        Task.FromResult(_subscriptions.TryGetValue(reference, out var stored) ? stored.Subscription : null);

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        string customerReference,
        string subscriptionReference,
        string productHandle,
        CancellationToken cancellationToken)
    {
        await Task.Delay(50, cancellationToken);
        var stored = _subscriptions.GetOrAdd(subscriptionReference, key =>
        {
            Interlocked.Increment(ref _subscriptionCreateCalls);
            var price = productHandle == "eshop-pro" ? 29900 : 2900;
            var name = productHandle == "eshop-pro" ? "Pro Plan" : "Basic Plan";
            return new StoredSubscription(
                customerReference,
                new MaxioSubscription(
                    Interlocked.Increment(ref _nextSubscriptionId),
                    key,
                    productHandle,
                    name,
                    price,
                    "USD",
                    "active",
                    DateTimeOffset.UtcNow.AddMonths(1),
                    1,
                    "month"));
        });
        return stored.Subscription;
    }

    public Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        string customerReference,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MaxioSubscription>>(
            _subscriptions.Values
                .Where(x => x.CustomerReference == customerReference)
                .Select(x => x.Subscription)
                .ToList());

    private sealed record StoredSubscription(string CustomerReference, MaxioSubscription Subscription);
}
