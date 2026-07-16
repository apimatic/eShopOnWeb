namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public int Quantity { get; set; }
    public string? Memo { get; set; }

    /// <summary>Set by the endpoint from the authenticated JWT principal — never trusted from client input.</summary>
    public string CustomerReference { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
}
