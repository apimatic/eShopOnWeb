using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeResponse : BaseResponse
{
    public PlanChangeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PlanChangeResponse()
    {
    }

    public string CurrentPlanHandle { get; set; } = string.Empty;
    public string TargetPlanHandle { get; set; } = string.Empty;
    public string Timing { get; set; } = string.Empty;

    public decimal ProratedAdjustment { get; set; }
    public decimal Charge { get; set; }
    public decimal PaymentDue { get; set; }
    public decimal CreditApplied { get; set; }

    /// <summary>Pass this back as <c>expectedNetAmount</c> when committing the change.</summary>
    public decimal NetAmount { get; set; }

    /// <summary>Populated on commit; null on a preview.</summary>
    public SubscriptionDto? Subscription { get; set; }

    internal void ApplyPreview(PlanChangePreview preview)
    {
        CurrentPlanHandle = preview.CurrentPlanHandle;
        TargetPlanHandle = preview.TargetPlanHandle;
        Timing = preview.Timing.ToString();
        ProratedAdjustment = preview.ProratedAdjustment;
        Charge = preview.Charge;
        PaymentDue = preview.PaymentDue;
        CreditApplied = preview.CreditApplied;
        NetAmount = preview.NetAmount;
    }
}
