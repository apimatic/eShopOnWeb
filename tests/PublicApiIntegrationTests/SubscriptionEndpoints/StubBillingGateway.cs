using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Stands in for Maxio so the endpoint tests exercise routing, authentication and response
/// shapes without reaching a real billing site.
/// </summary>
internal class StubBillingGateway : IBillingGateway
{
    public const string ProPlanHandle = "eshop-pro";

    private readonly Dictionary<string, BillingCustomer> _customers = new(StringComparer.Ordinal);
    private readonly List<CustomerSubscription> _subscriptions = new();
    private readonly object _sync = new();
    private int _nextId = 500;

    private static readonly SubscriptionPlan ProPlan = new()
    {
        Handle = ProPlanHandle,
        Name = "Pro Plan",
        PriceInCents = 29900,
        Currency = "USD",
        Interval = 1,
        IntervalUnit = "month",
        ProductFamilyHandle = "eshop-subscribe"
    };

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SubscriptionPlan>>(new[] { ProPlan });

    public Task<SubscriptionPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken = default) =>
        Task.FromResult(string.Equals(planHandle, ProPlanHandle, StringComparison.OrdinalIgnoreCase) ? ProPlan : null);

    public Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(_customers.TryGetValue(reference, out var customer) ? customer : null);
        }
    }

    public Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var created = new BillingCustomer
            {
                Id = ++_nextId,
                Reference = customer.Reference,
                Email = customer.Email,
                FirstName = customer.FirstName,
                LastName = customer.LastName
            };
            _customers[customer.Reference] = created;
            return Task.FromResult(created);
        }
    }

    public Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult<IReadOnlyList<CustomerSubscription>>(
                _subscriptions.Where(s => s.CustomerId == customerId).ToList());
        }
    }

    public Task<CustomerSubscription> CreateSubscriptionAsync(NewSubscription subscription, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            var created = new CustomerSubscription
            {
                Id = ++_nextId,
                State = SubscriptionStates.Active,
                CustomerId = subscription.CustomerId,
                Reference = subscription.Reference,
                PlanHandle = subscription.PlanHandle,
                PlanName = "Pro Plan",
                PriceInCents = 29900,
                Currency = "USD",
                Interval = 1,
                IntervalUnit = "month",
                CurrentPeriodStartedAt = now,
                CurrentPeriodEndsAt = now.AddMonths(1),
                NextBillingAt = now.AddMonths(1),
                ActivatedAt = now,
                CreatedAt = now,
                BalanceInCents = 29900,
                PaymentCollectionMethod = subscription.PaymentCollectionMethod
            };
            _subscriptions.Add(created);
            return Task.FromResult(created);
        }
    }

    public Task<CustomerSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(
                _subscriptions.FirstOrDefault(s => string.Equals(s.Reference, reference, StringComparison.Ordinal)));
        }
    }
}
