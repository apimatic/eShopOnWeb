using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    /// <summary>How many metered units were consumed. Must be greater than zero.</summary>
    public decimal Quantity { get; set; }

    public string? Memo { get; set; }

    /// <summary>Taken from the route, not the body.</summary>
    [JsonIgnore]
    public int SubscriptionId { get; set; }

    /// <summary>Taken from the access token: null for administrators, the caller otherwise.</summary>
    [JsonIgnore]
    public string? OwnerReference { get; set; }
}
