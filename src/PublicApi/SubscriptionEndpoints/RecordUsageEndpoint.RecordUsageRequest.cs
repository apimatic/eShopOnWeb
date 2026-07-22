namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    /// <summary>The subscription to bill. Taken from the route; ignored on the "mine" route.</summary>
    public int SubscriptionId { get; set; }

    /// <summary>Units consumed. Must be greater than zero.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Optional note stored alongside the usage entry.</summary>
    public string Memo { get; set; }
}
