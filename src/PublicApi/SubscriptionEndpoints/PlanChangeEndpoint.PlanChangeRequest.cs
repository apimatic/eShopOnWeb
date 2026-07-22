using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeRequest : BaseRequest
{
    /// <summary>The durable handle of the plan to move to.</summary>
    public string TargetPlanHandle { get; set; }

    /// <summary>"Immediate" (prorated, the default) or "NextRenewal" (no proration).</summary>
    public string Timing { get; set; }

    /// <summary>
    /// The token of the preview the customer confirmed. Required on commit; ignored on preview.
    /// </summary>
    public string PreviewToken { get; set; }

    /// <summary>Taken from the route, never from the request body.</summary>
    [JsonIgnore]
    public int SubscriptionId { get; set; }

    [JsonIgnore]
    public ClaimsPrincipal User { get; set; } = new();

    [JsonIgnore]
    public CancellationToken CancellationToken { get; set; }
}
