namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CancelSubscriptionRequest : BaseRequest
{
    public CancelSubscriptionRequest(int subscriptionId)
    {
        SubscriptionId = subscriptionId;
    }

    public int SubscriptionId { get; set; }
}
