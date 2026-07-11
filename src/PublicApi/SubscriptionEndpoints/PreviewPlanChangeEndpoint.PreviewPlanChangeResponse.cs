using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PreviewPlanChangeResponse : BaseResponse
{
    public PreviewPlanChangeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PreviewPlanChangeResponse()
    {
    }

    public PlanChangePreviewDto Preview { get; set; } = null!;
}
