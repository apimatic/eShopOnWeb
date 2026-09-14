namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    public string CallerReference { get; }

    public MySubscriptionsRequest(string callerReference)
    {
        CallerReference = callerReference;
    }
}
