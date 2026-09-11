namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    public string UserReference { get; set; } = string.Empty;
    public System.Threading.CancellationToken CancellationToken { get; set; } = default;
}
