using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Recurring-subscription billing, backed by the billing system of record (Maxio Advanced Billing).
/// This is the only surface eShopOnWeb code depends on; the provider SDK stays behind the Infrastructure
/// implementation. Failures surface as <see cref="Exceptions.MaxioBillingException"/>.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans available to subscribe to (the non-archived products in the configured family).</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for the given user (idempotently), then enrolls them in the
    /// requested plan. When <paramref name="planHandle"/> is null/blank the default plan is used. The call
    /// is safe to repeat: an existing non-terminal subscription to the same plan is returned as-is rather
    /// than creating a duplicate.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(SubscriberIdentity subscriber, string? planHandle, CancellationToken cancellationToken = default);

    /// <summary>Returns the subscriptions currently on file for the given user (empty if none / no customer yet).</summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
