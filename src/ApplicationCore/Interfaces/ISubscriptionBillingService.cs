using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing. The billing system is the system of record for plans and
/// subscriptions; this application keeps only the user ↔ billing-customer link and duplicate-prevention claims.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Plans currently offered (the configured product family's active, card-free products).</summary>
    Task<SubscriptionPlanCatalog> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for <paramref name="userName"/> and subscribes them to
    /// <paramref name="planHandle"/>. Idempotent per (user, plan): a repeat returns the existing subscription.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default);

    /// <summary>The caller's subscriptions as the billing system reports them.</summary>
    Task<CustomerSubscriptions> GetSubscriptionsAsync(string userName, CancellationToken cancellationToken = default);
}
