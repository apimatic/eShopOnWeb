namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    public MySubscriptionsRequest(string? userName)
    {
        UserName = userName;
    }

    /// <summary>The authenticated caller, taken from the bearer token's name claim.</summary>
    public string? UserName { get; }
}
