using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public SubscriptionDto? Subscription { get; init; }

    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }
}
