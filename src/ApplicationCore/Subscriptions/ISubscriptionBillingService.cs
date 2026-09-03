using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Recurring-subscription billing, backed by the billing system of record (Maxio Advanced Billing).
/// This abstraction lives in the domain layer and exposes only plain domain types, so callers (the
/// PublicApi endpoints) never reference the billing SDK. All methods raise
/// <see cref="SubscriptionBillingException"/> on any provider or transport failure.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the subscription plans available in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken ct);

    /// <summary>
    /// Ensures a billing customer exists for <paramref name="subscriber"/> (idempotently) and
    /// enrolls them in the given plan. If the subscriber already has a live subscription to the
    /// plan, that existing subscription is returned instead of creating a duplicate, so a
    /// double-click never creates two customers or two subscriptions.
    /// </summary>
    /// <param name="subscriber">The authenticated eShop user (from the JWT, never request input).</param>
    /// <param name="planHandle">
    /// The plan handle to subscribe to; when null/blank the configured default plan is used.
    /// </param>
    Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string? planHandle, CancellationToken ct);

    /// <summary>
    /// Lists the subscriber's subscriptions as reflected by the billing system. Returns an empty
    /// list when the subscriber has no billing customer yet.
    /// </summary>
    Task<IReadOnlyList<BillingSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken ct);
}
