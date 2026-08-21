using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);

    Task<SubscribeResult> SubscribeAsync(
        BillingCustomer customer,
        string? productHandle,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<UserSubscription>> ListMySubscriptionsAsync(
        BillingCustomer customer,
        CancellationToken cancellationToken);
}
