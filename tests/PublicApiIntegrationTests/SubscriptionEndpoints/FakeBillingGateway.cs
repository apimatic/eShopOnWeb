using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>An in-process stand-in for Maxio so endpoint tests never touch the network.</summary>
public class FakeBillingGateway : IBillingGateway
{
    public const string TimeoutPlan = "plan-timeout";
    public const string RejectedPlan = "plan-rejected";

    private int _nextId = 1000;
    private readonly ConcurrentDictionary<string, BillingCustomerAccount> _customers = new();
    private readonly ConcurrentDictionary<string, (int CustomerId, BillingSubscription Subscription)> _subscriptions = new();

    public int CreateSubscriptionCalls;

    public static readonly IReadOnlyList<SubscriptionPlan> Plans = new[]
    {
        new SubscriptionPlan(1, "plan-a", "Plan A", "First plan", 29900, 1, "month"),
        new SubscriptionPlan(2, "plan-b", "Plan B", null, 2900, 1, "month"),
        new SubscriptionPlan(3, "plan-c", "Plan C", null, 1000, 1, "month"),
        new SubscriptionPlan(4, TimeoutPlan, "Timeout Plan", null, 100, 1, "month"),
        new SubscriptionPlan(5, RejectedPlan, "Rejected Plan", null, 100, 1, "month")
    };

    public Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new SubscriptionPlanCatalog(Plans, IsTruncated: false));

    public Task<BillingCustomerAccount?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken) =>
        Task.FromResult(_customers.TryGetValue(reference, out var account) ? account : null);

    public Task<BillingCustomerAccount> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken) =>
        Task.FromResult(_customers.GetOrAdd(customer.Reference,
            reference => new BillingCustomerAccount(Interlocked.Increment(ref _nextId), reference)));

    public async Task<BillingSubscription> CreateSubscriptionAsync(NewBillingSubscription subscription, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref CreateSubscriptionCalls);
        await Task.Delay(50, cancellationToken); // widen the window for concurrent requests
        if (subscription.PlanHandle == TimeoutPlan)
        {
            throw BillingProviderException.NoResponse("Maxio did not respond while creating the subscription.", outcomeUnknown: true);
        }

        if (subscription.PlanHandle == RejectedPlan)
        {
            throw new BillingProviderException(BillingFailureKind.Rejected, "Maxio rejected the request while creating the subscription.",
                422, new[] { "Product is not available." });
        }

        var plan = Plans.Single(p => p.Handle == subscription.PlanHandle);
        var created = new BillingSubscription(Interlocked.Increment(ref _nextId), subscription.Reference, "active",
            plan.Handle, plan.Name, plan.PriceInCents, "USD", plan.Interval, plan.IntervalUnit,
            DateTimeOffset.UtcNow.AddMonths(1), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        _subscriptions[subscription.Reference] = (subscription.CustomerId, created);
        return created;
    }

    public Task<BillingSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken) =>
        Task.FromResult(_subscriptions.TryGetValue(reference, out var entry) ? entry.Subscription : null);

    public Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BillingSubscription>>(_subscriptions.Values
            .Where(s => s.CustomerId == customerId)
            .Select(s => s.Subscription)
            .ToList());
}
