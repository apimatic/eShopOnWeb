using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing. Maxio Advanced Billing is the system of record.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<CustomerSubscription> SubscribeAsync(
        BillingShopper shopper,
        string productHandle,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerSubscription>> GetMySubscriptionsAsync(
        BillingShopper shopper,
        CancellationToken cancellationToken = default);
}
