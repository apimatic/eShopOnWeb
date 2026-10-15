using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionService
{
    Task<SubscriptionPlanCatalog> GetPlansAsync(CancellationToken cancellationToken);

    /// <summary>Ensures a billing customer exists for the user and subscribes them to the plan, at most once.</summary>
    Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<BillingSubscription>> GetSubscriptionsAsync(string userName, CancellationToken cancellationToken);
}
