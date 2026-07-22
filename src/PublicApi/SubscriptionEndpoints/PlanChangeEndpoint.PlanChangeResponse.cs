using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeResponse : BaseResponse
{
    public PlanChangeResponse(Guid correlationId) : base(correlationId) { }

    public PlanChangeResponse() { }

    public SubscriptionDto Subscription { get; set; }
}

public class PlanChangePreviewResponse : BaseResponse
{
    public PlanChangePreviewResponse(Guid correlationId) : base(correlationId) { }

    public PlanChangePreviewResponse() { }

    public PlanChangePreviewDto Preview { get; set; }
}
