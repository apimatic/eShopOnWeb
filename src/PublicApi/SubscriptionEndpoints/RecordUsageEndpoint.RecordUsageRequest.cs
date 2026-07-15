namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public int Quantity { get; set; }
    public string? Memo { get; set; }

    /// <summary>Overwritten server-side from the authenticated principal — never trust a client-supplied value.</summary>
    public string UserReference { get; set; } = string.Empty;

    /// <summary>Overwritten server-side from the caller's role — never trust a client-supplied value.</summary>
    public bool IsAdmin { get; set; }
}
