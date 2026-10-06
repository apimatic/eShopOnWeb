using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Deterministic stand-in for the Maxio billing system of record, used to exercise
/// the subscription endpoints without outbound calls. Mirrors the invariants the real
/// provider enforces: unique customer reference, and active subscriptions per customer/plan.
/// </summary>
public class InMemorySubscriptionBillingClient : ISubscriptionBillingClient
{
    private static readonly List<BillingPlan> Catalog = new()
    {
        new BillingPlan { Id = 1, Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month", RequiresPaymentProfile = false },
        new BillingPlan { Id = 2, Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month", RequiresPaymentProfile = false },
        new BillingPlan { Id = 3, Handle = "card-required-plan", Name = "Card Plan", PriceInCents = 1000, Interval = 1, IntervalUnit = "month", RequiresPaymentProfile = true },
    };

    private readonly ConcurrentDictionary<string, BillingCustomer> _customers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<NewBillingSubscription> _creationAttempts = new();
    private int _nextCustomerId;
    private int _nextSubscriptionId = 1000;

    public IReadOnlyList<BillingPlan> Plans => Catalog;
    public int CustomerCount => _customers.Count;
    public IEnumerable<NewBillingSubscription> CreationAttempts => _creationAttempts;

    public List<BillingSubscription> Subscriptions = new();
    private readonly SemaphoreSlim _subscriptionWriteLock = new(1, 1);

    public Task<IReadOnlyList<BillingPlan>> GetPlansAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BillingPlan>>(Catalog);

    public Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default) =>
        Task.FromResult(_customers.TryGetValue(reference, out var customer) ? customer : null);

    public Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken = default)
    {
        if (!_customers.TryAdd(customer.Reference, new BillingCustomer
            {
                Id = Interlocked.Increment(ref _nextCustomerId),
                Reference = customer.Reference,
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
            }))
        {
            throw new SubscriptionBillingException(SubscriptionBillingErrorKind.Rejected, "Reference has already been taken");
        }

        return Task.FromResult(_customers[customer.Reference]);
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(NewBillingSubscription subscription, CancellationToken cancellationToken = default)
    {
        _creationAttempts.Enqueue(subscription);

        await _subscriptionWriteLock.WaitAsync(cancellationToken);
        try
        {
            var plan = Catalog.First(p => string.Equals(p.Handle, subscription.ProductHandle, StringComparison.OrdinalIgnoreCase));
            var created = new BillingSubscription
            {
                Id = Interlocked.Increment(ref _nextSubscriptionId),
                State = "active",
                CustomerId = subscription.CustomerId,
                PlanHandle = plan.Handle,
                PlanName = plan.Name,
                PriceInCents = plan.PriceInCents,
                Interval = plan.Interval,
                IntervalUnit = plan.IntervalUnit,
                PaymentCollectionMethod = subscription.PaymentCollectionMethod ?? "automatic",
                CurrentPeriodStartsAt = DateTimeOffset.UtcNow,
                CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
                NextBillingDate = DateTimeOffset.UtcNow.AddMonths(1),
                ActivatedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            Subscriptions.Add(created);
            return created;
        }
        finally
        {
            _subscriptionWriteLock.Release();
        }
    }

    public Task<IReadOnlyList<BillingSubscription>> GetSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        List<BillingSubscription> forCustomer;
        lock (Subscriptions)
        {
            forCustomer = Subscriptions.Where(s => s.CustomerId == customerId).ToList();
        }

        return Task.FromResult<IReadOnlyList<BillingSubscription>>(forCustomer);
    }
}
