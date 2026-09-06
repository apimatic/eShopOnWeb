namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    public string? UserId { get; set; }
    public string? Email { get; set; }
}
