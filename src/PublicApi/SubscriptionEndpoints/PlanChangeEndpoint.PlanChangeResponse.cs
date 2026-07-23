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

    /// <summary>Populated by the preview route.</summary>
    public PlanChangePreviewDto? Preview { get; set; }

    /// <summary>Populated by the commit route.</summary>
    public SubscriptionDto? Subscription { get; set; }
}
