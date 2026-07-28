namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    public MySubscriptionsRequest()
    {
    }

    public MySubscriptionsRequest(string? subscriberReference)
    {
        SubscriberReference = subscriberReference;
    }

    /// <summary>The subscriber's stable reference — set by the endpoint from the authenticated token.</summary>
    public string? SubscriberReference { get; set; }
}
