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

    public PlanChangePreviewDto? Preview { get; set; }
}

public class PlanChangeResponse : BaseResponse
{
    public PlanChangeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PlanChangeResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }

    public string PreviousPlanHandle { get; set; } = string.Empty;

    public string NewPlanHandle { get; set; } = string.Empty;

    /// <summary>Proration actually applied, in whole currency units.</summary>
    public decimal ProrationAmount { get; set; }

    public DateTimeOffset? EffectiveAt { get; set; }

    /// <summary>The freshly re-priced preview that was verified against the confirmation and applied.</summary>
    public PlanChangePreviewDto? AppliedPreview { get; set; }
}
