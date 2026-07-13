using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ChangePlanResponse : BaseResponse
{
    public ChangePlanResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ChangePlanResponse()
    {
    }

    public SubscriptionDto Subscription { get; set; } = new();
}
