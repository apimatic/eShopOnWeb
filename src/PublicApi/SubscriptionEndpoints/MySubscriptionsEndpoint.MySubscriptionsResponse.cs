using System;
using System.Linq;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public MySubscriptionsResponse()
    {
    }

    public System.Collections.Generic.List<SubscriptionItemDto> Subscriptions { get; set; } =
        new System.Collections.Generic.List<SubscriptionItemDto>();
}
