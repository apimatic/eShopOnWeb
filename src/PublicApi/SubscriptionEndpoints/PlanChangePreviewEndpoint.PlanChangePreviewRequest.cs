using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangePreviewRequest : BaseRequest
{
    [Required]
    public int SubscriptionId { get; set; }

    [Required]
    public string TargetPlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// <c>Immediately</c> prorates against the current period; <c>AtNextRenewal</c> defers the
    /// change to the next period with no proration.
    /// </summary>
    public PlanChangeTiming Timing { get; set; } = PlanChangeTiming.Immediately;
}
