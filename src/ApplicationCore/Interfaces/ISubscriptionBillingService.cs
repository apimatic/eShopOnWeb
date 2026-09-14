using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, with an external billing system as the system of record.
/// This capability runs in parallel to the one-time Catalog / Basket / Order flow.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the plans a shopper can subscribe to, i.e. the active products of the configured
    /// product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the plan with the given handle, or null when the catalog has no such plan.</summary>
    Task<SubscriptionPlan?> GetPlanAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the subscriber in a plan, creating the billing-system customer first if needed.
    /// <para>
    /// The operation is idempotent: repeating it while the shopper already holds a live subscription
    /// to the same plan returns that subscription with <see cref="SubscribeResult.Created"/> false
    /// instead of enrolling them twice.
    /// </para>
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the subscriber's subscriptions. Read-only: it never creates a customer record for a
    /// shopper who has not subscribed yet.
    /// </summary>
    Task<SubscriberSubscriptions> GetSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default);
}
