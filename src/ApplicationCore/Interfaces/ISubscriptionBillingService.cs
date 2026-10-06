using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external billing system.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the shopper to the plan. Idempotent per shopper: repeating the call for the same plan returns
    /// the existing subscription instead of creating another.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(string buyerId, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(string buyerId, CancellationToken cancellationToken = default);
}
