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

    public int SubscriptionId { get; set; }
    public string? CurrentPlanHandle { get; set; }
    public string TargetPlanHandle { get; set; } = string.Empty;
    public bool Prorate { get; set; }

    /// <summary>Credit for the unused remainder of the current plan, in the site currency.</summary>
    public decimal ProratedAdjustment { get; set; }

    /// <summary>Charge raised for the new plan, in the site currency.</summary>
    public decimal Charge { get; set; }

    /// <summary>Net amount owed once adjustment and credit are applied, in the site currency.</summary>
    public decimal PaymentDue { get; set; }

    public decimal CreditApplied { get; set; }
}
