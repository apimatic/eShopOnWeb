using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangePreviewResponse : BaseResponse
{
    public PlanChangePreviewResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PlanChangePreviewResponse()
    {
    }

    public string TargetPlanHandle { get; set; }
    public decimal ProratedAdjustment { get; set; }
    public decimal Charge { get; set; }

    /// <summary>What the customer pays now — echo this back on commit to guard against a stale preview.</summary>
    public decimal PaymentDue { get; set; }

    public decimal CreditApplied { get; set; }
}
