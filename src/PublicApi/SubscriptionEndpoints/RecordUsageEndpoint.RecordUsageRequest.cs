namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : AuthenticatedSubscriptionRequest
{
    /// <summary>Taken from the route.</summary>
    public int SubscriptionId { get; set; }

    /// <summary>Units consumed. Must be greater than zero.</summary>
    public decimal Quantity { get; set; }

    public string Memo { get; set; }
}
