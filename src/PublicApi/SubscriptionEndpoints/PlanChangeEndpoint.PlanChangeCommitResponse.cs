using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeCommitResponse : BaseResponse
{
    public PlanChangeCommitResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionDto Subscription { get; set; } = null!;
}
