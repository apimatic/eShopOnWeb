using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionMappingStore
{
    Task SyncAsync(string userId, MaxioCustomer customer, IReadOnlyList<MaxioSubscription> subscriptions,
        CancellationToken cancellationToken);
}
