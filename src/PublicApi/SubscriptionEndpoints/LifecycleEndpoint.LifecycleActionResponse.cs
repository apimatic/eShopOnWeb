using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleActionResponse : BaseResponse
{
    public LifecycleActionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public LifecycleActionResponse()
    {
    }

    public SubscriptionDto Subscription { get; set; } = new();
}
