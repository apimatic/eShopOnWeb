namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseMessage
{
    /// <summary>Units consumed. Must be greater than zero.</summary>
    public int Quantity { get; set; }

    /// <summary>Optional note stored alongside the usage record.</summary>
    public string? Memo { get; set; }

    /// <summary>Taken from the route, not the body.</summary>
    public int SubscriptionId { get; private set; }

    /// <summary>
    /// Null for an administrator (any subscription), otherwise the caller's own reference so the service
    /// refuses a subscription that is not theirs.
    /// </summary>
    public string? RestrictToUserReference { get; private set; }

    public void SetContext(int subscriptionId, string? restrictToUserReference)
    {
        SubscriptionId = subscriptionId;
        RestrictToUserReference = restrictToUserReference;
    }
}
