using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing capability, backed by an external billing system of
/// record (Maxio Advanced Billing). This is an additive, parallel capability to the
/// existing one-time Catalog → Basket → Order flow.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the plans available for subscription within the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the given user to a plan. Ensures a billing customer exists for the user
    /// (idempotent on the user's <see cref="SubscriberIdentity.Reference"/>) and enrols them
    /// in the plan. Idempotent per (user, plan): a repeated call while an active subscription
    /// to the same plan exists returns that subscription rather than creating a duplicate.
    /// </summary>
    /// <param name="subscriber">The user to subscribe.</param>
    /// <param name="planHandle">
    /// The handle of the plan to subscribe to. When null/blank the service falls back to the
    /// demo default plan; the handle is always validated against the live product family.
    /// </param>
    Task<SubscriptionDetails> SubscribeAsync(
        SubscriberIdentity subscriber,
        string? planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the subscriptions currently held by the given user. Returns an empty list
    /// when the user has no billing customer record yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default);
}
