using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PreviewPlanChangeRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public string TargetProductHandle { get; set; } = string.Empty;
    public bool ApplyNow { get; set; }
    public string? OwnerReference { get; set; }
}

public class PreviewPlanChangeResponse : BaseResponse
{
    public PreviewPlanChangeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PreviewPlanChangeResponse()
    {
    }

    public PlanChangePreviewDto Preview { get; set; } = new();
}
