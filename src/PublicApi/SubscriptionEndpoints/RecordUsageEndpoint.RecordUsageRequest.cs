namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    public int Quantity { get; set; }
    public string? Memo { get; set; }

    /// <summary>Route/identity-derived — never bound from the request body.</summary>
    public int SubscriptionId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
}
