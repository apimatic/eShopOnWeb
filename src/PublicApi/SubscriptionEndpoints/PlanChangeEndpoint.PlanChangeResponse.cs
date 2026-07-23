using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeResponse : BaseResponse
{
    public PlanChangeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PlanChangeResponse()
    {
    }

    /// <summary>Set on the preview route.</summary>
    public PlanChangePreviewDto? Preview { get; set; }

    /// <summary>Set on the commit route.</summary>
    public SubscriptionDto? Subscription { get; set; }
}
