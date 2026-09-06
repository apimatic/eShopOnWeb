using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

/// <summary>
/// In-memory stand-in for Maxio that reproduces the behaviour the subscribe flow relies on:
/// references are unique, and creating a record with one that is taken fails the same way
/// the real API fails.
/// </summary>
internal class FakeBillingGateway : IBillingGateway
{
    private readonly ConcurrentDictionary<string, BillingCustomer> _customersByReference = new(StringComparer.Ordinal);
    private readonly List<CustomerSubscription> _subscriptions = new();
    private readonly object _sync = new();
    private int _nextId = 1000;

    public List<SubscriptionPlan> Plans { get; } = new();

    public int CreateCustomerCalls;
    public int CreateSubscriptionCalls;

    /// <summary>Runs just before a create is applied, to stage a race.</summary>
    public Func<Task>? BeforeCreateCustomer { get; set; }

    public Func<Task>? BeforeCreateSubscription { get; set; }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SubscriptionPlan>>(Plans.ToList());

    public Task<SubscriptionPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken = default) =>
        Task.FromResult(Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase)));

    public Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default) =>
        Task.FromResult(_customersByReference.TryGetValue(reference, out var customer) ? customer : null);

    public async Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref CreateCustomerCalls);

        if (BeforeCreateCustomer is not null)
        {
            await BeforeCreateCustomer();
        }

        var created = new BillingCustomer
        {
            Id = Interlocked.Increment(ref _nextId),
            Reference = customer.Reference,
            Email = customer.Email,
            FirstName = customer.FirstName,
            LastName = customer.LastName
        };

        if (!_customersByReference.TryAdd(customer.Reference, created))
        {
            throw DuplicateReference();
        }

        return created;
    }

    public Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult<IReadOnlyList<CustomerSubscription>>(
                _subscriptions.Where(s => s.CustomerId == customerId).ToList());
        }
    }

    public async Task<CustomerSubscription> CreateSubscriptionAsync(NewSubscription subscription, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref CreateSubscriptionCalls);

        if (BeforeCreateSubscription is not null)
        {
            await BeforeCreateSubscription();
        }

        lock (_sync)
        {
            if (subscription.Reference is not null
                && _subscriptions.Any(s => string.Equals(s.Reference, subscription.Reference, StringComparison.Ordinal)))
            {
                throw DuplicateReference();
            }

            var created = new CustomerSubscription
            {
                Id = Interlocked.Increment(ref _nextId),
                State = SubscriptionStates.Active,
                CustomerId = subscription.CustomerId,
                PlanHandle = subscription.PlanHandle,
                PlanName = subscription.PlanHandle,
                Reference = subscription.Reference,
                PaymentCollectionMethod = subscription.PaymentCollectionMethod,
                CreatedAt = DateTimeOffset.UtcNow,
                NextBillingAt = DateTimeOffset.UtcNow.AddMonths(1)
            };

            _subscriptions.Add(created);
            return created;
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

    public void SeedCustomer(BillingCustomer customer) =>
        _customersByReference[customer.Reference!] = customer;

    public void SeedSubscription(CustomerSubscription subscription)
    {
        lock (_sync)
        {
            _subscriptions.Add(subscription);
        }
    }

    public int SubscriptionCount
    {
        get
        {
            lock (_sync)
            {
                return _subscriptions.Count;
            }
        }
    }

    private static BillingGatewayException DuplicateReference() =>
        new("The billing system rejected the request.",
            statusCode: 422,
            errors: new[] { "Reference: must be unique - that value has been taken." },
            isDuplicateReference: true);
}
