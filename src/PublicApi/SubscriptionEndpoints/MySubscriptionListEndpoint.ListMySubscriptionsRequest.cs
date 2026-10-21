using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    public SubscriberIdentity Subscriber { get; init; }

    public ListMySubscriptionsRequest(SubscriberIdentity subscriber)
    {
        Subscriber = subscriber;
    }
}
