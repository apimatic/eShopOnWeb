using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Subscriptions;

namespace PublicApiIntegrationTests;

public sealed class FakeMaxioBillingGateway : IMaxioBillingGateway
{
    private readonly ConcurrentDictionary<string, BillingCustomer> _customers = new();
    private readonly ConcurrentDictionary<string, BillingSubscription> _subscriptions = new();
    private int _createSubscriptionCalls;

    public int CreateSubscriptionCalls => _createSubscriptionCalls;

    public void Reset()
    {
        _customers.Clear();
        _subscriptions.Clear();
        Interlocked.Exchange(ref _createSubscriptionCalls, 0);
    }

    public Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BillingPlan>>(new[]
        {
            new BillingPlan(101, "eshop-pro", "Pro Plan", "Professional subscription", 29900, 1, "month", false),
            new BillingPlan(102, "basic-plan", "Basic Plan", "Basic subscription", 2900, 1, "month", false)
        });

    public Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        _customers.TryGetValue(reference, out var customer);
        return Task.FromResult(customer);
    }

    public Task<BillingCustomer> EnsureCustomerAsync(
        string reference,
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken)
    {
        var customer = _customers.GetOrAdd(reference, key => new BillingCustomer(201, key));
        return Task.FromResult(customer);
    }

    public Task<BillingSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        _subscriptions.TryGetValue(reference, out var subscription);
        return Task.FromResult(subscription);
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string reference,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _createSubscriptionCalls);
        await Task.Delay(50, cancellationToken);
        return _subscriptions.GetOrAdd(reference, _ => new BillingSubscription(
            301,
            productHandle,
            productHandle == "eshop-pro" ? "Pro Plan" : "Basic Plan",
            productHandle == "eshop-pro" ? 29900 : 2900,
            "active",
            new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero),
            "USD",
            reference));
    }

    public Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BillingSubscription>>(_subscriptions.Values.ToList());
}
