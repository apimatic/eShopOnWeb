using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangePreviewRequest : BaseRequest
{
    /// <summary>The subscription being quoted. Taken from the route.</summary>
    [JsonIgnore]
    public int SubscriptionId { get; set; }

    /// <summary>The handle of the plan to quote a move to.</summary>
    public string TargetPlanHandle { get; set; } = string.Empty;

    /// <summary>Quote applying now with proration, or at the next renewal without it.</summary>
    public PlanChangeTiming Timing { get; set; } = PlanChangeTiming.ImmediateWithProration;
}
