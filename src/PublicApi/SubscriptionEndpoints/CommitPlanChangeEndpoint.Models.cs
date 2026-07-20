using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CommitPlanChangeRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public string TargetProductHandle { get; set; } = string.Empty;
    public bool ApplyNow { get; set; }

    /// <summary>Exactly the preview the caller was shown by <see cref="PreviewPlanChangeEndpoint"/>.</summary>
    public PlanChangePreviewDto PreviouslyShownPreview { get; set; } = new();

    public string? OwnerReference { get; set; }
}

public class CommitPlanChangeResponse : BaseResponse
{
    public CommitPlanChangeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CommitPlanChangeResponse()
    {
    }

    public SubscriptionDto Subscription { get; set; } = new();
}
