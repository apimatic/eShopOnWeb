using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionDetails> SubscribeAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDetails>> ListForUserAsync(
        BillingUser user,
        CancellationToken cancellationToken);
}
