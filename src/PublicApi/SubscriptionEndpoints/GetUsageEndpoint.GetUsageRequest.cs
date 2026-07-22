namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetUsageRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
}
