using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscribeResult> SubscribeAsync(
        string userName,
        string productHandle,
        string? pricePointHandle,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDetails>> ListForUserAsync(
        string userName,
        CancellationToken cancellationToken);
}
