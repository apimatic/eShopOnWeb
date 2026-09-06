using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-plan enrollment for eShopOnWeb shoppers. Runs alongside the one-time
/// catalog/basket/order flow and shares nothing with it.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>The plans a shopper can subscribe to.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes a shopper to a plan, creating their billing customer record first if needed.
    /// Idempotent: repeating the call returns the enrollment that already exists rather than
    /// creating a second customer or a second subscription.
    /// </summary>
    /// <param name="subscriber">The shopper, taken from the authenticated caller.</param>
    /// <param name="planHandle">Plan to subscribe to, or null for the configured default plan.</param>
    /// <param name="idempotencyKey">
    /// Caller-supplied key that makes a client-side retry safe. When omitted a fresh key is used,
    /// so a retry is deduplicated by the existing-enrollment check instead.
    /// </param>
    Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber,
        string? planHandle = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>Every subscription the shopper holds, read back from the billing system of record.</summary>
    Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default);
}
