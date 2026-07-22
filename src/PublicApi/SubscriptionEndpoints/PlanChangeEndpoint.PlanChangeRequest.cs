using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A plan change the customer has confirmed. The four cent amounts must be echoed back exactly as
/// the preview returned them.
/// </summary>
public class PlanChangeRequest : BaseRequest
{
    [Required]
    public int SubscriptionId { get; set; }

    [Required]
    public string TargetPlanHandle { get; set; } = string.Empty;

    public PlanChangeTiming Timing { get; set; } = PlanChangeTiming.Immediately;

    public long ProratedAdjustmentInCents { get; set; }
    public long ChargeInCents { get; set; }
    public long PaymentDueInCents { get; set; }
    public long CreditAppliedInCents { get; set; }
}
