using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangePreviewDto
{
    public int SubscriptionId { get; set; }
    public string CurrentPlanHandle { get; set; } = string.Empty;
    public string TargetPlanHandle { get; set; } = string.Empty;
    public string Timing { get; set; } = string.Empty;
    public decimal ProratedAdjustment { get; set; }
    public decimal Charge { get; set; }
    public decimal CreditApplied { get; set; }
    public decimal PaymentDue { get; set; }
    public decimal NewPlanPrice { get; set; }
    public long PaymentDueInCents { get; set; }
    public DateTimeOffset? EffectiveAt { get; set; }

    /// <summary>
    /// Echo this back when committing the change. A commit whose token no longer matches a fresh
    /// preview is rejected rather than applied at a different amount.
    /// </summary>
    public string Token { get; set; } = string.Empty;
}
