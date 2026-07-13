using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CommitPlanChangeResponse : BaseResponse
{
    public CommitPlanChangeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CommitPlanChangeResponse()
    {
    }

    public BillingSubscriptionDto Subscription { get; set; } = new();
}
