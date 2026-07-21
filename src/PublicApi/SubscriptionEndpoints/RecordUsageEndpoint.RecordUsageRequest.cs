namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }

    /// <summary>Set by the route handler from the authenticated caller's identity — never bound from the request body.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Set by the route handler from the authenticated caller's role — never bound from the request body.</summary>
    public bool IsAdministrator { get; set; }
}
