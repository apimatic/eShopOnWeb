using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public interface ISubscriptionRecordStore
{
    Task<SubscriptionRecord?> GetAsync(string userId, string productHandle, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionRecord>> ListAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> TryAddAsync(SubscriptionRecord record, CancellationToken cancellationToken = default);
    Task SaveAsync(SubscriptionRecord record, CancellationToken cancellationToken = default);
}
