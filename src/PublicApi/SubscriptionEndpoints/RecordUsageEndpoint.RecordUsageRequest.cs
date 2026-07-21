namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    /// <summary>Admin-only override — the subscription to record usage against. Omit to record against the caller's own active subscription.</summary>
    public int? SubscriptionId { get; set; }

    public int Quantity { get; set; }
    public string? Memo { get; set; }
}
