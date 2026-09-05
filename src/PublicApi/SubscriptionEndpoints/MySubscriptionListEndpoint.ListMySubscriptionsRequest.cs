namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    public ListMySubscriptionsRequest(string customerEmail)
    {
        CustomerEmail = customerEmail;
    }

    /// <summary>The authenticated caller's identity, taken from the JWT.</summary>
    public string CustomerEmail { get; }
}
