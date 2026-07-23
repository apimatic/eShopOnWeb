namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    public int SubscriptionId { get; set; }

    /// <summary>Units consumed. Must be greater than zero.</summary>
    public decimal Quantity { get; set; }

    public string Memo { get; set; }
}
