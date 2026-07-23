namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    /// <summary>Units consumed. Must be greater than zero.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Optional note recorded alongside the usage.</summary>
    public string Memo { get; set; }

    /// <summary>Administrators only: report usage for another user.</summary>
    public string UserReference { get; set; }
}
