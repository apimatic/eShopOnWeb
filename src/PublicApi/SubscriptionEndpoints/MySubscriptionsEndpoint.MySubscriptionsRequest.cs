namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    /// <summary>Populated by the route handler from the authenticated caller; not caller-supplied.</summary>
    public string UserName { get; set; } = string.Empty;
}
