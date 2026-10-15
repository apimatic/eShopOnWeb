using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing. Failures surface as <see cref="BillingException"/>.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<SubscriptionPlanList> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionInfo>> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken = default);
}
