using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    /// <summary>The number of metered units consumed. Must be greater than zero.</summary>
    public decimal Quantity { get; set; }

    /// <summary>An optional note recorded alongside the usage.</summary>
    public string Memo { get; set; }

    /// <summary>Taken from the route, never from the request body.</summary>
    [JsonIgnore]
    public int SubscriptionId { get; set; }

    /// <summary>The authenticated caller, used for the ownership check.</summary>
    [JsonIgnore]
    public ClaimsPrincipal User { get; set; } = new();

    [JsonIgnore]
    public CancellationToken CancellationToken { get; set; }
}
