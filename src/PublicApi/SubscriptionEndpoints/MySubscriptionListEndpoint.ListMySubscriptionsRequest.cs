using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Internal request for <see cref="MySubscriptionListEndpoint"/>. The subscriber comes from the
/// bearer token, so there is nothing to bind from the wire.
/// </summary>
public class ListMySubscriptionsRequest : BaseRequest
{
    public ListMySubscriptionsRequest(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        Subscriber = subscriber;
        CancellationToken = cancellationToken;
    }

    public SubscriberIdentity Subscriber { get; }

    public CancellationToken CancellationToken { get; }
}
