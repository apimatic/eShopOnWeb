namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request for the caller's subscriptions. Not bound from the body — constructed from the JWT identity.
/// </summary>
public class MySubscriptionsRequest : BaseRequest
{
    public MySubscriptionsRequest(string customerReference)
    {
        CustomerReference = customerReference;
    }

    /// <summary>The Maxio customer reference (the eShop user's stable key) whose subscriptions to return.</summary>
    public string CustomerReference { get; }
}
