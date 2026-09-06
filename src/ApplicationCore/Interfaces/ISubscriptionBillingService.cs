using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, delegated to an external billing system of record.
/// eShopOnWeb keeps no local copy of plans, customers or subscriptions: the provider is queried
/// on every call so the answer is always authoritative.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Plans that are currently offered, i.e. the non-archived products of the configured family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the subscriber in a plan. Ensures a billing customer exists for the eShopOnWeb user
    /// first. The operation is idempotent: repeating it returns the existing subscription rather
    /// than creating a second one.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscriptions belonging to the subscriber, newest first. Returns an empty list when the user
    /// has never been enrolled (no billing customer exists yet).
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
