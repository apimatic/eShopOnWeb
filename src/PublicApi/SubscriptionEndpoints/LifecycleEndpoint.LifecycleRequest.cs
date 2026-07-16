namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleRequest : BaseRequest
{
    public int SubscriptionId { get; set; }

    /// <summary>One of "pause", "resume", "cancel", "reactivate" (case-insensitive).</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Only meaningful for "cancel": true = at end of period, false = immediate.</summary>
    public bool EndOfPeriod { get; set; }
    public string? Reason { get; set; }

    /// <summary>Set by the endpoint from the authenticated JWT principal — never trusted from client input.</summary>
    public string CustomerReference { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
}
