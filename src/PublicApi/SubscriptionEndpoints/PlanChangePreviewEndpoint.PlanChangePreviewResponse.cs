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

    public string FromPlanHandle { get; set; } = string.Empty;
    public string ToPlanHandle { get; set; } = string.Empty;
    public bool ApplyNow { get; set; }
    public decimal ProratedAmount { get; set; }
    public decimal PaymentDueAmount { get; set; }
    public decimal CreditAppliedAmount { get; set; }
    public DateTimeOffset EffectiveDate { get; set; }
}
