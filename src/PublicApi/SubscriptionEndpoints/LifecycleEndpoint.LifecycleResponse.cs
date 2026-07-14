using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleResponse : BaseResponse
{
    public LifecycleResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionDto Subscription { get; set; } = null!;
}
