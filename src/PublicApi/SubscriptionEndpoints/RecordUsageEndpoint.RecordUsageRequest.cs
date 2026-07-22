using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    /// <summary>The subscription the usage accrues to. Taken from the route.</summary>
    [JsonIgnore]
    public int SubscriptionId { get; set; }

    /// <summary>The number of metered units consumed. Must be positive.</summary>
    public decimal Quantity { get; set; }

    public string? Memo { get; set; }
}
