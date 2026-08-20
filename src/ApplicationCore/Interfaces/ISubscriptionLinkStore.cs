using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionLinkStore
{
    Task<SubscriptionLink?> FindAsync(string userId, string productHandle, CancellationToken cancellationToken);
    Task SaveAsync(SubscriptionLink link, CancellationToken cancellationToken);
}
