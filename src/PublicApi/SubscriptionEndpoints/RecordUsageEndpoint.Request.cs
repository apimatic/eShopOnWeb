using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : AuthenticatedSubscriptionRequest
{
    /// <summary>The subscription to bill. Taken from the route, not the body.</summary>
    [JsonIgnore]
    public int SubscriptionId { get; set; }

    /// <summary>
    /// True when the caller holds the administrator role, in which case any subscription may be
    /// billed. Set from the bearer token, never from the body.
    /// </summary>
    [JsonIgnore]
    public bool IsAdministrator { get; set; }

    /// <summary>Units consumed. Must be greater than zero.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Optional note recorded alongside the usage.</summary>
    public string? Memo { get; set; }
}
