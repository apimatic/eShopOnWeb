using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscriptionEnrollment> SubscribeAsync(
        SubscriptionUser user,
        string productHandle,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserSubscription>> GetSubscriptionsAsync(
        SubscriptionUser user,
        CancellationToken cancellationToken = default);
}
