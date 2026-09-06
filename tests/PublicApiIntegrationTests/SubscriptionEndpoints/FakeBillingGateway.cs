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

/// <summary>
/// An in-memory stand-in for Maxio, so the endpoint tests exercise the real routing, auth,
/// identity resolution and status-code mapping without depending on a live billing site.
/// </summary>
/// <remarks>
/// Deliberately as permissive as the real thing: it will happily create a second subscription to
/// the same plan for the same customer if asked. Preventing that is the service's job, and these
/// tests are here to prove it happens.
/// </remarks>
public class FakeBillingGateway : IBillingGateway
{
    public const string ProPlanHandle = "eshop-pro";
    public const string BasicPlanHandle = "basic-plan";

    private readonly ConcurrentDictionary<string, BillingCustomer> _customersByReference = new();
    private readonly ConcurrentDictionary<int, List<CustomerSubscription>> _subscriptionsByCustomer = new();
    private readonly ConcurrentDictionary<string, byte> _seenUniquenessTokens = new();
    private int _nextCustomerId = 1000;
    private int _nextSubscriptionId = 5000;

    public int CustomersCreated;
    public int SubscriptionsCreated;

    /// <summary>Set to make the next create call report a duplicate submission.</summary>
    public bool RejectNextSubmissionAsDuplicate { get; set; }

    public Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<SubscriptionPlan>>(new[]
        {
            new SubscriptionPlan(BasicPlanHandle, "Basic Plan", null, 2900, 1, "month", null, false, "eshop-subscribe"),
            new SubscriptionPlan(ProPlanHandle, "Pro Plan", null, 29900, 1, "month", null, false, "eshop-subscribe")
        });

    public async Task<SubscriptionPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        var plans = await ListPlansAsync(cancellationToken);
        return plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    public Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default) =>
        Task.FromResult(_customersByReference.TryGetValue(reference, out var customer) ? customer : null);

    public Task<BillingCustomer> CreateCustomerAsync(NewCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var customer = new BillingCustomer(Interlocked.Increment(ref _nextCustomerId), request.Reference,
            request.Email, request.FirstName, request.LastName);

        if (!_customersByReference.TryAdd(request.Reference, customer))
            throw new DuplicateBillingReferenceException(request.Reference);

        Interlocked.Increment(ref CustomersCreated);
        return Task.FromResult(customer);
    }

    public Task<IReadOnlyCollection<CustomerSubscription>> ListCustomerSubscriptionsAsync(int customerId,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = _subscriptionsByCustomer.GetOrAdd(customerId, _ => new List<CustomerSubscription>());
        lock (subscriptions)
        {
            return Task.FromResult<IReadOnlyCollection<CustomerSubscription>>(subscriptions.ToList());
        }
    }

    public Task<CustomerSubscription> CreateSubscriptionAsync(NewSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (RejectNextSubmissionAsDuplicate)
        {
            RejectNextSubmissionAsDuplicate = false;
            throw new DuplicateBillingSubmissionException(request.UniquenessToken);
        }

        if (!_seenUniquenessTokens.TryAdd(request.UniquenessToken, 0))
            throw new DuplicateBillingSubmissionException(request.UniquenessToken);

        var now = DateTimeOffset.UtcNow;
        var subscription = new CustomerSubscription(
            Interlocked.Increment(ref _nextSubscriptionId),
            request.Reference,
            SubscriptionStates.Active,
            request.PlanHandle,
            request.PlanHandle == ProPlanHandle ? "Pro Plan" : "Basic Plan",
            request.PlanHandle == ProPlanHandle ? 29900 : 2900,
            1, "month",
            now, now.AddMonths(1), now.AddMonths(1), now, null,
            request.PaymentCollectionMethod,
            request.CustomerId,
            _customersByReference.Values.FirstOrDefault(c => c.Id == request.CustomerId)?.Reference);

        var subscriptions = _subscriptionsByCustomer.GetOrAdd(request.CustomerId, _ => new List<CustomerSubscription>());
        lock (subscriptions)
        {
            subscriptions.Add(subscription);
        }

        Interlocked.Increment(ref SubscriptionsCreated);
        return Task.FromResult(subscription);
    }

    /// <summary>Adds a subscription directly, for setting up a state the endpoint should read back.</summary>
    public void SeedSubscription(int customerId, CustomerSubscription subscription)
    {
        var subscriptions = _subscriptionsByCustomer.GetOrAdd(customerId, _ => new List<CustomerSubscription>());
        lock (subscriptions)
        {
            subscriptions.Add(subscription);
        }
    }
}
