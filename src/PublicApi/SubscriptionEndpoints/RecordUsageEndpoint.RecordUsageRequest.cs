namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    public int Quantity { get; set; }
    public string? Memo { get; set; }

    /// <summary>Admin-only: record usage against a specific subscription instead of the caller's
    /// own. Overwritten by the route handler with the resolved, authorization-checked id.</summary>
    public int? SubscriptionId { get; set; }
}
