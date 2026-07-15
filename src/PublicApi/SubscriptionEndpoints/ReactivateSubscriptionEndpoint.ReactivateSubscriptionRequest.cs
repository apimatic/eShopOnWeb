namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ReactivateSubscriptionRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public string? OwnerUserId { get; set; }
}
