using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Recurring-subscription billing; the billing system of record is external.</summary>
public interface ISubscriptionService
{
    Task<PlanListResult> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <exception cref="SubscriptionException"/>
    Task<SubscribeResult> SubscribeAsync(string userName, string email, string planHandle, CancellationToken cancellationToken = default);

    /// <exception cref="SubscriptionException"/>
    Task<MySubscriptionsResult> ListMySubscriptionsAsync(string userName, CancellationToken cancellationToken = default);
}
