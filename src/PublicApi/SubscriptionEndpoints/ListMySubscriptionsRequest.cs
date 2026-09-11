namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    public string UserId { get; set; } = string.Empty;
}
