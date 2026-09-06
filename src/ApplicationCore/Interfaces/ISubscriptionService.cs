using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external billing system of record.
/// </summary>
/// <remarks>
/// This capability runs in parallel with the one-time Catalog / Basket / Order flow and shares
/// no state with it. eShopOnWeb stores nothing about subscriptions locally: the provider owns
/// plans, customers and enrollments, and every read goes back to it.
/// </remarks>
public interface ISubscriptionService
{
    /// <summary>Plans currently offered, newest pricing first read from the provider.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for the shopper and enrolls them in the requested plan.
    /// </summary>
    /// <remarks>
    /// Idempotent: repeated calls (a double-clicked button, a retried request) return the
    /// existing subscription with <see cref="SubscribeResult.Created"/> set to false rather than
    /// enrolling twice.
    /// </remarks>
    /// <exception cref="Exceptions.SubscriptionPlanNotFoundException">
    /// The plan handle is not offered by the configured product family.
    /// </exception>
    /// <exception cref="Exceptions.BillingProviderException">The provider rejected or failed the call.</exception>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// All subscriptions belonging to the shopper, most recently created first. Returns an empty
    /// list when the shopper has never subscribed.
    /// </summary>
    Task<IReadOnlyList<Subscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default);
}
