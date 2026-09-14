using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing capability, backed by an external billing system of
/// record (Maxio Advanced Billing). This is an additive, parallel capability to the
/// existing one-time commerce flow and does not replace it.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the subscription plans available to shoppers (the products in the configured
    /// Maxio product family).
    /// </summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the shopper in the given plan. Ensures a Maxio customer exists for the
    /// shopper (idempotent by reference) and creates the subscription. If the shopper is
    /// already enrolled in the plan, the existing subscription is returned unchanged, so
    /// repeated calls (e.g. a double click) never create duplicates.
    /// </summary>
    Task<SubscriptionResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the shopper's subscriptions. Returns an empty collection when the shopper has
    /// no Maxio customer yet.
    /// </summary>
    Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
