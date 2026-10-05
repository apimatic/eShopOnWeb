using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionService
{
    Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken);

    Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, string? firstName, string? lastName,
        CancellationToken cancellationToken);

    Task<MySubscriptions> GetMySubscriptionsAsync(string userName, CancellationToken cancellationToken);
}
