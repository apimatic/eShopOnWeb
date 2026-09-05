using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken ct = default);

    Task<CustomerSubscription> SubscribeAsync(string userReference, string planHandle, CancellationToken ct = default);

    Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(string userReference, CancellationToken ct = default);
}
