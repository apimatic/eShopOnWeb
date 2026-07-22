using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleRequest : BaseRequest
{
    /// <summary>
    /// One of "Pause", "Resume", "Cancel", "CancelAtEndOfPeriod", or "Reactivate".
    /// </summary>
    public string Action { get; set; }

    /// <summary>An optional reason recorded with the transition.</summary>
    public string Reason { get; set; }

    /// <summary>Taken from the route, never from the request body.</summary>
    [JsonIgnore]
    public int SubscriptionId { get; set; }

    [JsonIgnore]
    public ClaimsPrincipal User { get; set; } = new();

    [JsonIgnore]
    public CancellationToken CancellationToken { get; set; }
}
