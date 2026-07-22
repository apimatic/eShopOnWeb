namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : AuthenticatedSubscriptionRequest
{
    /// <summary>Number of metered units consumed. Must be greater than zero.</summary>
    public int Quantity { get; set; }

    /// <summary>Optional note stored with the usage record.</summary>
    public string? Memo { get; set; }

    /// <summary>
    /// Target subscription. Omit to report against the caller's own active subscription; supplying it
    /// targets any subscription and therefore requires the administrators role.
    /// </summary>
    public int? SubscriptionId { get; set; }
}
