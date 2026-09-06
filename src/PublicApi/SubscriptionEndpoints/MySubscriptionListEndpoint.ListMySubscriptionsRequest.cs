using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Input for <see cref="MySubscriptionListEndpoint"/>: the authenticated subscriber and the
/// request's cancellation token, both resolved by the route delegate.
/// </summary>
public class ListMySubscriptionsRequest : BaseRequest
{
    public ListMySubscriptionsRequest(Subscriber subscriber, CancellationToken cancellationToken)
    {
        Subscriber = subscriber;
        CancellationToken = cancellationToken;
    }

    public Subscriber Subscriber { get; }

    public CancellationToken CancellationToken { get; }
}
