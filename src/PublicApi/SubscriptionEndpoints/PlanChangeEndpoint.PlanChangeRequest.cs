using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeRequest : BaseRequest
{
    /// <summary>The subscription being moved. Taken from the route.</summary>
    [JsonIgnore]
    public int SubscriptionId { get; set; }

    /// <summary>The handle of the plan to move to.</summary>
    public string TargetPlanHandle { get; set; } = string.Empty;

    /// <summary>Apply now with proration, or at the next renewal without it.</summary>
    public PlanChangeTiming Timing { get; set; } = PlanChangeTiming.ImmediateWithProration;

    /// <summary>
    /// The payment due the customer confirmed. When supplied, the change is re-quoted first and
    /// rejected if the amount has moved since the preview.
    /// </summary>
    public decimal? ConfirmedPaymentDue { get; set; }
}
