using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the subscription capability against the billing system of record.
/// Idempotency contract: repeated requests for the same shopper and plan resolve to
/// a single billing customer and a single non-terminal subscription. This holds
/// because (a) Maxio enforces one customer per application reference, and we key the
/// customer on the shopper's account email, (b) an existing non-terminal subscription
/// for the same plan is returned instead of creating a second one, and (c) concurrent
/// attempts for the same shopper are serialized in-process.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    /// <summary>
    /// Payment collection used for plans that do not require a stored payment method,
    /// so signup succeeds without card capture (the customer is invoiced instead).
    /// </summary>
    private const string NO_PAYMENT_METHOD_COLLECTION = "remittance";

    /// <summary>
    /// Per-shopper locks that keep double-click / concurrent signup races from
    /// producing two subscriptions. In-process only; a multi-instance deployment
    /// would use a distributed lock or lean on caller-supplied idempotency keys.
    /// </summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ShopperLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly ISubscriptionBillingClient _billingClient;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(ISubscriptionBillingClient billingClient, IAppLogger<SubscriptionService> logger)
    {
        _billingClient = billingClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        return await _billingClient.GetPlansAsync(cancellationToken);
    }

    public async Task<SubscribeOutcome> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(request);
        Guard.Against.NullOrWhiteSpace(request.UserReference, nameof(request.UserReference));
        Guard.Against.NullOrWhiteSpace(request.PlanHandle, nameof(request.PlanHandle));

        var plan = await ResolvePlanAsync(request.PlanHandle, cancellationToken);

        if (plan.RequiresPaymentProfile)
        {
            throw new SubscriptionBillingException(SubscriptionBillingErrorKind.Rejected,
                $"Plan '{plan.Handle}' requires a payment method, which this subscription flow does not collect.");
        }

        var shopperLock = ShopperLocks.GetOrAdd(request.UserReference, _ => new SemaphoreSlim(1, 1));
        await shopperLock.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(request, cancellationToken);

            var existingSubscriptions = await _billingClient.GetSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
            var liveSubscription = existingSubscriptions.FirstOrDefault(subscription =>
                !BillingSubscription.IsEndOfLife(subscription.State) &&
                string.Equals(subscription.PlanHandle, plan.Handle, StringComparison.OrdinalIgnoreCase));

            if (liveSubscription is not null)
            {
                _logger.LogInformation($"Subscriber '{request.UserReference}' already has subscription {liveSubscription.Id} on plan '{plan.Handle}'; returning it unchanged.");
                return new SubscribeOutcome(liveSubscription, WasNewlyCreated: false);
            }

            var subscription = await _billingClient.CreateSubscriptionAsync(new NewBillingSubscription
            {
                ProductHandle = plan.Handle,
                CustomerId = customer.Id,
                PaymentCollectionMethod = NO_PAYMENT_METHOD_COLLECTION,
                IdempotencyKey = request.IdempotencyKey,
            }, cancellationToken);

            _logger.LogInformation($"Subscriber '{request.UserReference}' enrolled in plan '{plan.Handle}' as subscription {subscription.Id}.");
            return new SubscribeOutcome(subscription, WasNewlyCreated: true);
        }
        finally
        {
            shopperLock.Release();
        }
    }

    public async Task<IReadOnlyList<BillingSubscription>> GetSubscriptionsForUserAsync(string userReference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));

        var customer = await _billingClient.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<BillingSubscription>();
        }

        return await _billingClient.GetSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
    }

    private async Task<BillingPlan> ResolvePlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        var plans = await _billingClient.GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));

        if (plan is null)
        {
            throw new SubscriptionBillingException(SubscriptionBillingErrorKind.PlanNotFound,
                $"Subscription plan '{planHandle}' is not offered in the current billing catalog.");
        }

        return plan;
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(SubscribeRequest request, CancellationToken cancellationToken)
    {
        var customer = await _billingClient.FindCustomerByReferenceAsync(request.UserReference, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        var (firstName, lastName) = DeriveCustomerNames(request);

        try
        {
            _logger.LogInformation($"Creating billing customer for subscriber '{request.UserReference}'.");
            return await _billingClient.CreateCustomerAsync(new NewBillingCustomer
            {
                Reference = request.UserReference,
                FirstName = firstName,
                LastName = lastName,
                Email = request.Email,
            }, cancellationToken);
        }
        catch (SubscriptionBillingException ex) when (ex.Kind == SubscriptionBillingErrorKind.Rejected)
        {
            // The most likely rejection here is a concurrent request that won the race to
            // create this customer (the billing system enforces one customer per reference).
            var racedCustomer = await _billingClient.FindCustomerByReferenceAsync(request.UserReference, cancellationToken);
            if (racedCustomer is not null)
            {
                _logger.LogWarning($"Billing customer for subscriber '{request.UserReference}' already existed after a rejected create; continuing with it.");
                return racedCustomer;
            }

            throw;
        }
    }

    private static (string FirstName, string LastName) DeriveCustomerNames(SubscribeRequest request)
    {
        var firstName = request.FirstName?.Trim();
        var lastName = request.LastName?.Trim();

        if (string.IsNullOrWhiteSpace(firstName))
        {
            var localPart = (request.Email ?? string.Empty).Split('@')[0];
            firstName = string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart;
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            lastName = "Shopper";
        }

        return (firstName, lastName);
    }
}
