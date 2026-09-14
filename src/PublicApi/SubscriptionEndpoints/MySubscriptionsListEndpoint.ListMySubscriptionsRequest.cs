using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    public ListMySubscriptionsRequest(Subscriber subscriber, CancellationToken cancellationToken = default)
    {
        Subscriber = subscriber;
        CancellationToken = cancellationToken;
    }

    /// <summary>Resolved from the bearer token, never from caller supplied input.</summary>
    public Subscriber Subscriber { get; }

    public CancellationToken CancellationToken { get; }
}
