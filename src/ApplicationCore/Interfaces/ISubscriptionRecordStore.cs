using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionRecordStore
{
    Task SynchronizeAsync(string userId, SubscriptionDetails subscription, CancellationToken cancellationToken = default);
}
