using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing against the external billing system of record.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<CustomerSubscription> SubscribeAsync(
        string userId,
        string email,
        string userName,
        string productHandle,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default);
}
