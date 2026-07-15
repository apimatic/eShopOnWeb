using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Round-tripped by the caller: a preview returned from the preview endpoint is sent back verbatim
/// to the commit endpoint to prove the customer confirmed exactly this pricing (UC3's staleness check).
/// </summary>
public class PlanChangePreviewDto
{
    public int SubscriptionId { get; set; }
    public string CurrentProductHandle { get; set; } = string.Empty;
    public string TargetProductHandle { get; set; } = string.Empty;
    public string Timing { get; set; } = string.Empty;
    public long? ProratedAdjustmentInCents { get; set; }
    public long? ChargeInCents { get; set; }
    public long? PaymentDueInCents { get; set; }
    public long? CreditAppliedInCents { get; set; }
    public long NewPlanPriceInCents { get; set; }
    public DateTimeOffset? EffectiveAt { get; set; }
}
