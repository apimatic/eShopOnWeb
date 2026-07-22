namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    /// <summary>Units consumed. Must be greater than zero.</summary>
    public decimal Quantity { get; set; }

    public string? Memo { get; set; }

    /// <summary>
    /// Target another customer's subscription. Administrators only; omit to meter your own.
    /// </summary>
    public int? SubscriptionId { get; set; }

    /// <summary>Set from the caller's token.</summary>
    public string UserReference { get; set; }

    /// <summary>Set from the caller's token.</summary>
    public bool IsAdministrator { get; set; }
}
