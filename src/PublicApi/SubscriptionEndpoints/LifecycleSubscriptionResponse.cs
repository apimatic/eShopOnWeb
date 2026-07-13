using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Shared response shape for the four UC4 lifecycle actions (pause/resume/cancel/reactivate).</summary>
public class LifecycleSubscriptionResponse : BaseResponse
{
    public LifecycleSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public LifecycleSubscriptionResponse()
    {
    }

    public BillingSubscriptionDto Subscription { get; set; } = new();
}
