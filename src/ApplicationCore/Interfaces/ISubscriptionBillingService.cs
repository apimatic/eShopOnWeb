using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external billing system of record.
/// eShopOnWeb stores no plan or subscription state of its own: every read goes to the provider,
/// which keeps the integration correct across restarts and across app instances.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the plans a shopper may subscribe to, from the configured catalog.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrols <paramref name="subscriber"/> in <paramref name="planHandle"/>, creating the billing
    /// customer first if one does not already exist.
    /// </summary>
    /// <remarks>
    /// Idempotent per (subscriber, plan): repeating the call — a double-clicked button, a client
    /// retry — returns the existing enrollment with <see cref="SubscribeResult.Created"/> false
    /// instead of creating a second customer or a second subscription.
    /// </remarks>
    /// <param name="planHandle">Plan to subscribe to, or null to use the configured default plan.</param>
    Task<SubscribeResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string? planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription belonging to <paramref name="subscriber"/>, newest first.
    /// Returns an empty list when the shopper has no billing customer record yet.
    /// </summary>
    Task<IReadOnlyList<SubscriberSubscription>> ListSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default);
}
