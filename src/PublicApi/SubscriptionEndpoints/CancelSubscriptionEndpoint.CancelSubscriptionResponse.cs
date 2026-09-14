using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CancelSubscriptionResponse : BaseResponse
{
    public CancelSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionDto Subscription { get; set; } = new();
}
