using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse() : base() { }
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }

    public SubscriptionDto? Subscription { get; set; }
}
