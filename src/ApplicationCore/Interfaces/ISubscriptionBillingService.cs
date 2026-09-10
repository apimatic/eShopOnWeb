using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external billing system of record
/// (Maxio Advanced Billing). This is an additive capability alongside the existing
/// one-time Catalog → Basket → Order flow; it does not replace it.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the plans a shopper can subscribe to (the products in the configured
    /// product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrols <paramref name="subscriber"/> in the plan identified by
    /// <paramref name="planHandle"/> (or the default plan when it is null/empty).
    /// The operation is idempotent: the underlying customer is found-or-created by its
    /// stable reference, and a repeated enrolment in the same plan returns the existing
    /// subscription instead of creating a duplicate.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(SubscriberIdentity subscriber, string? planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions currently owned by <paramref name="subscriber"/>. Returns an
    /// empty list when the user has never been enrolled (no billing-system customer yet).
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsForUserAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
