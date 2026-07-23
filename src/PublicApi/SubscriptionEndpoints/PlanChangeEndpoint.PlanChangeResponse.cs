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

    public SubscriptionDto? Subscription { get; set; }
}
