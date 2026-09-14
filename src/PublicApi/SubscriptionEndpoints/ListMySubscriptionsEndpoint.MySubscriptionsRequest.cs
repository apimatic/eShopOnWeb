using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request for listing the caller's subscriptions. Carries only the subscriber resolved server-side
/// from the authenticated caller's token; there is no client-supplied body.
/// </summary>
public class MySubscriptionsRequest : BaseRequest
{
    public MySubscriptionsRequest(SubscriberInfo subscriber)
    {
        Subscriber = subscriber;
    }

    public SubscriberInfo Subscriber { get; }
}
