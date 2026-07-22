namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    /// <summary>
    /// Taken from the bearer token, not from the caller's payload.
    /// </summary>
    internal string? UserName { get; set; }
}
