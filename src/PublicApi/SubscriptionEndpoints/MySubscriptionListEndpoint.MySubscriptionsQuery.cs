using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>The caller's billing identity, resolved from the bearer token.</summary>
public class MySubscriptionsQuery : BaseRequest
{
    public MySubscriptionsQuery(SubscriberIdentity subscriber) => Subscriber = subscriber;

    public SubscriberIdentity Subscriber { get; }
}
