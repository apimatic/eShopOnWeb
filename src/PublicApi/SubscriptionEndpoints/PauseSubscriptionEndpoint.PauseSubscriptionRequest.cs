namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PauseSubscriptionRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public string? OwnerUserId { get; set; }
}
