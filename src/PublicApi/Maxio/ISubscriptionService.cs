using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface ISubscriptionService
{
    Task<MaxioProductFamily?> GetProductFamilyAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken);

    Task<SubscriptionEnrollment> SubscribeAsync(SubscriberProfile subscriber, string planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(string subscriberReference, CancellationToken cancellationToken);
}
