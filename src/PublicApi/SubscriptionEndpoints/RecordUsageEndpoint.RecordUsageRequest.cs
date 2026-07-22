namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    /// <summary>Number of units consumed. Must be greater than zero.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Optional note stored alongside the usage.</summary>
    public string? Memo { get; set; }

    /// <summary>Bound from the route, never from the body.</summary>
    public int SubscriptionId { get; set; }

    /// <summary>Resolved from the bearer token; null for an administrator.</summary>
    public string? OwnerReference { get; set; }

    /// <summary>False when the request carried no usable identity.</summary>
    public bool IsAuthenticated { get; set; }
}
