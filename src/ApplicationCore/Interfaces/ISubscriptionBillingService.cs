using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external billing system of record.
/// <para>
/// Implementations must be idempotent: enrolling the same subscriber on the same plan twice yields
/// one customer and one subscription.
/// </para>
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Plans the shopper can subscribe to, taken from the configured product catalog.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the subscriber on a plan, creating the billing customer first if necessary.
    /// Returns the existing subscription when the subscriber is already enrolled on that plan.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every subscription belonging to the subscriber, newest first. Empty when the subscriber has
    /// no billing customer record yet.
    /// </summary>
    Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
