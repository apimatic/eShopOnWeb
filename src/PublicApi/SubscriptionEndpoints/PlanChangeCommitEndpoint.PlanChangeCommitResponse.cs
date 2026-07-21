using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeCommitResponse : BaseResponse
{
    public PlanChangeCommitResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PlanChangeCommitResponse()
    {
    }

    public SubscriptionDto Subscription { get; set; } = new();
}
