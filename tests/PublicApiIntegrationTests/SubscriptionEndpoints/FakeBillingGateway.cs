using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>In-memory stand-in for the billing provider, counting the writes that reach it.</summary>
public sealed class FakeBillingGateway : ISubscriptionBillingGateway
{
    private readonly ConcurrentDictionary<string, BillingSubscription> _byReference = new();
    private int _nextId = 1000;
    private int _createSubscriptionCalls;
    private int _createCustomerCalls;

    public SubscriptionPlanCatalog Catalog { get; set; } = new(new[]
    {
        new SubscriptionPlan("plan-created", "Created", null, 29900, 1, "month"),
        new SubscriptionPlan("plan-race", "Race", null, 2900, 1, "month"),
        new SubscriptionPlan("plan-unknown", "Unknown", null, 2900, 1, "month"),
        new SubscriptionPlan("plan-rejected", "Rejected", null, 2900, 1, "month"),
    }, Truncated: false);

    /// <summary>Delay inside CreateSubscription, to hold a request in flight while another arrives.</summary>
    public TimeSpan CreateDelay { get; set; } = TimeSpan.Zero;

    /// <summary>When set, CreateSubscription invokes it instead of succeeding (and may record the write first).</summary>
    public Func<string, Exception?>? CreateSubscriptionFailure { get; set; }

    /// <summary>When true, a failing CreateSubscription still "lands" at the provider (an ambiguous write).</summary>
    public bool FailedWriteLands { get; set; }

    public int CreateSubscriptionCalls => _createSubscriptionCalls;
    public int CreateCustomerCalls => _createCustomerCalls;

    public Task<SubscriptionPlanCatalog> GetPlanCatalogAsync(CancellationToken cancellationToken) => Task.FromResult(Catalog);

    public Task<int?> FindCustomerIdAsync(string customerReference, CancellationToken cancellationToken) =>
        Task.FromResult<int?>(_createCustomerCalls > 0 ? 42 : null);

    public Task<int> CreateCustomerAsync(string customerReference, BillingSubscriber subscriber, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _createCustomerCalls);
        return Task.FromResult(42);
    }

    public Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsAsync(int customerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BillingSubscription>>(_byReference.Values.ToList());

    public async Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string planHandle, string subscriptionReference, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _createSubscriptionCalls);
        await Task.Delay(CreateDelay, CancellationToken.None);

        var failure = CreateSubscriptionFailure?.Invoke(subscriptionReference);
        if (failure is not null)
        {
            if (FailedWriteLands)
            {
                Record(planHandle, subscriptionReference);
            }
            throw failure;
        }
        return Record(planHandle, subscriptionReference);
    }

    public Task<BillingSubscription?> FindSubscriptionAsync(string subscriptionReference, CancellationToken cancellationToken) =>
        Task.FromResult(_byReference.TryGetValue(subscriptionReference, out var found) ? found : null);

    private BillingSubscription Record(string planHandle, string reference)
    {
        var subscription = new BillingSubscription(Interlocked.Increment(ref _nextId), planHandle, planHandle, 2900, "USD",
            "active", true, DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow, reference);
        _byReference[reference] = subscription;
        return subscription;
    }
}
