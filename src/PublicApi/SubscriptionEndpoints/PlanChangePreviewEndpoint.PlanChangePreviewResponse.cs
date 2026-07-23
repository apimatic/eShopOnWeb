using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>All amounts are in whole currency units.</summary>
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

    public decimal PaymentDue { get; set; }

    public decimal CreditApplied { get; set; }
}
