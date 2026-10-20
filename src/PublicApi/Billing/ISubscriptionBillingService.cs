using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Recurring-subscription billing. Failures surface as <see cref="BillingException"/>.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<SubscriptionPlanCatalog> GetPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscribeResult> SubscribeAsync(BillingBuyer buyer, string planHandle, CancellationToken cancellationToken = default);

    Task<BuyerSubscriptions> GetSubscriptionsAsync(BillingBuyer buyer, CancellationToken cancellationToken = default);
}
