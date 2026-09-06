using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : SubscriptionEndpointRequest
{
    public ListMySubscriptionsRequest(SubscriberIdentity subscriber)
    {
        Subscriber = subscriber;
    }

    /// <summary>The authenticated caller. Resolved from the bearer token, never from the request.</summary>
    public SubscriberIdentity Subscriber { get; }
}
