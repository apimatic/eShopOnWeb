using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Server-side request for the caller's subscriptions. Constructed from the authenticated principal,
/// never bound from the wire, so a caller cannot ask for somebody else's subscriptions.
/// </summary>
public class MySubscriptionsRequest : BaseRequest
{
    public MySubscriptionsRequest(SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        Subscriber = subscriber;
        CancellationToken = cancellationToken;
    }

    public SubscriberIdentity Subscriber { get; }

    public CancellationToken CancellationToken { get; }
}
