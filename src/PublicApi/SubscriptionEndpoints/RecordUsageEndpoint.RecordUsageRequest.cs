namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public int Quantity { get; set; }
    public string? Memo { get; set; }

    /// <summary>Null when the caller is an admin (acts on any subscription); otherwise the caller's own id, enforced server-side.</summary>
    public string? OwnerUserId { get; set; }
}
