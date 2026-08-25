using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.SubscriptionBilling;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates subscription billing for eShopOnWeb shoppers against Maxio
/// Advanced Billing, which is the billing system of record.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the subscription plans available in the configured Maxio product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the shopper to a plan. Idempotent: ensures exactly one Maxio
    /// customer per eShopOnWeb user (via customer reference) and returns the
    /// existing live subscription when the shopper is already subscribed to the
    /// plan instead of creating a duplicate.
    /// Returns null when the plan handle is not offered.
    /// </summary>
    Task<SubscriptionDetails?> SubscribeAsync(SubscriberInfo subscriber, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the shopper's subscriptions. Empty when the shopper has no Maxio customer yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(SubscriberInfo subscriber, CancellationToken cancellationToken = default);
}
