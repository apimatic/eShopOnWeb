using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing. The billing system is the system of record for plans and subscriptions;
/// failures it reports surface as <see cref="Exceptions.BillingProviderException"/>.
/// </summary>
public interface ISubscriptionService
{
    Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken = default);

    Task<CustomerSubscriptions> ListSubscriptionsAsync(string buyerId, CancellationToken cancellationToken = default);
}
