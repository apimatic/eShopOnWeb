namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansRequest : BaseRequest
{
    public System.Threading.CancellationToken CancellationToken { get; set; } = default;
}
