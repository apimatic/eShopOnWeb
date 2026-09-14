using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Internal request for listing the caller's subscriptions. Not bound from HTTP: the endpoint
/// resolves the authenticated caller from the JWT and constructs this before calling HandleAsync.
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
