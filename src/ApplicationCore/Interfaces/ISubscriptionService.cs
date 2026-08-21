using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);
    Task<SubscribeResult> SubscribeAsync(BillingUser user, string productHandle, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionDetails>> ListForUserAsync(BillingUser user, CancellationToken cancellationToken = default);
}
