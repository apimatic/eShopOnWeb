namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CancelSubscriptionRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public bool EndOfPeriod { get; set; }
    public string? Reason { get; set; }
    public string? OwnerReference { get; set; }
}
