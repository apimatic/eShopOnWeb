using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    /// <summary>Units consumed. Zero and negative values are rejected before any provider call.</summary>
    public int Quantity { get; set; }

    public string? Memo { get; set; }

    /// <summary>Taken from the route, not the body.</summary>
    [JsonIgnore]
    public long SubscriptionId { get; set; }
}
