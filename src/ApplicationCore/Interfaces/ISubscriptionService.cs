using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionService
{
    Task<PlanCatalog> GetPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscribeResult> SubscribeAsync(BillingBuyer buyer, string planHandle, CancellationToken cancellationToken = default);

    Task<BuyerSubscriptions> GetSubscriptionsAsync(BillingBuyer buyer, CancellationToken cancellationToken = default);

    /// <summary>Settles enrollments whose billing outcome was never confirmed. Returns how many were examined.</summary>
    Task<int> SettleUnconfirmedAsync(CancellationToken cancellationToken = default);
}
