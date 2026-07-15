namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ResumeSubscriptionRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public string? OwnerUserId { get; set; }
}
