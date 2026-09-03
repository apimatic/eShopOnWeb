using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Recurring-subscription billing, backed by Maxio Advanced Billing. This abstraction is the seam between
/// eShopOnWeb and the billing provider: the API layer depends only on this interface, the Maxio SDK lives
/// behind the implementation in Infrastructure. Every method throws
/// <see cref="Exceptions.MaxioBillingException"/> on failure (provider errors and transport faults are
/// translated there), so callers handle one failure type with a caller-safe message.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the subscribable plans (non-archived products) in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes <paramref name="subscriber"/> to a plan. Ensures a Maxio customer exists for the user
    /// (idempotent), then enrolls them (idempotent) — a double-click never creates a second customer or a
    /// second subscription. <paramref name="planHandle"/> selects the plan; when null the configured default
    /// plan is used.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(SubscriberIdentity subscriber, string? planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriptions belonging to <paramref name="subscriber"/> (empty if none / no customer yet).</summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
