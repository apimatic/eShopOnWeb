using Microsoft.eShopWeb.MaxioBilling.Models;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    public ListMySubscriptionsRequest(SubscriberIdentity subscriber)
    {
        Subscriber = subscriber;
    }

    /// <summary>The caller, taken from the bearer token.</summary>
    public SubscriberIdentity Subscriber { get; }
}
