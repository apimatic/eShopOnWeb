using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscription;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing as the billing system of record.
/// Implementations must be idempotent with respect to the eShopOnWeb user id: repeated
/// calls must never create duplicate customers or duplicate subscriptions.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the plans shoppers can subscribe to (the products of the configured product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the given eShopOnWeb user. Idempotent:
    /// the customer is keyed by the user id, so concurrent or repeated calls resolve
    /// to a single Maxio customer.
    /// </summary>
    Task<SubscriptionCustomerResult> EnsureCustomerAsync(SubscriptionCustomerRequest customer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls a user in a plan. Idempotent: if the user already has a live subscription
    /// to the product, that subscription is returned instead of creating a duplicate.
    /// </summary>
    Task<SubscriptionDetails> SubscribeAsync(SubscriptionSignupRequest signup, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions the given eShopOnWeb user has in the billing system.
    /// </summary>
    Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default);
}