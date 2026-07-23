using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    /// <summary>Taken from the route, never from the request body.</summary>
    [JsonIgnore]
    public int SubscriptionId { get; set; }

    /// <summary>Units consumed. Must be greater than zero.</summary>
    public decimal Quantity { get; set; }

    public string? Memo { get; set; }
}
