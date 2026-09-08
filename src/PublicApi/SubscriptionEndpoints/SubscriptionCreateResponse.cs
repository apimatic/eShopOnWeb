using System;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionCreateResponse : BaseResponse
{
    public SubscriptionCreateResponse(Guid correlationId) : base(correlationId) { }

    public SubscriptionCreateResponse()
    {
    }

    public SubscriptionDto Subscription { get; set; } = new SubscriptionDto();
}
