using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external billing system of record.
/// </summary>
/// <remarks>
/// Every method converts provider failures — API errors, transport failures and unreadable responses —
/// into <see cref="Exceptions.BillingProviderException"/>. No provider-specific exception escapes.
/// </remarks>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans available for subscription in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes <paramref name="subscriber"/> to <paramref name="planHandle"/>, creating the
    /// provider-side customer record first if it does not exist yet.
    /// </summary>
    /// <param name="planHandle">
    /// Stable plan handle. When null or empty, the configured default plan is used.
    /// </param>
    /// <remarks>
    /// Idempotent: repeating the call (a double-click, a retry) neither creates a second customer nor a
    /// second subscription on the same plan.
    /// </remarks>
    Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string? planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription held by <paramref name="subscriber"/>.</summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(Subscriber subscriber,
        CancellationToken cancellationToken = default);
}
