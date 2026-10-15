using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing. The billing provider is the system of record for plans and subscriptions.
/// Failures surface as <see cref="Exceptions.SubscriptionBillingException"/>.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken = default);

    Task<CustomerSubscriptions> GetSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default);
}
