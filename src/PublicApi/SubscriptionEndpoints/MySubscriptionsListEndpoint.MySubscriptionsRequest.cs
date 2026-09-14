namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    public MySubscriptionsRequest(string customerReference)
    {
        CustomerReference = customerReference;
    }

    public string CustomerReference { get; }
}
