namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public double Quantity { get; set; }
    public string? Memo { get; set; }

    // Null for an Administrator (may record usage on any subscription); otherwise the
    // caller's own identity, enforced as the subscription owner.
    public string? OwnerReference { get; set; }
}
