using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionService
{
    Task<PlanCatalog> GetPlansAsync(CancellationToken cancellationToken);

    Task<SubscribeResult> SubscribeAsync(string userName, string? planHandle, CancellationToken cancellationToken);

    Task<CustomerSubscriptions> GetSubscriptionsAsync(string userName, CancellationToken cancellationToken);
}
