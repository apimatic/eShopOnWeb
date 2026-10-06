using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsListResponse : BaseResponse
{
    public MySubscriptionsListResponse(Guid correlationId) : base(correlationId)
    {
    }

    public MySubscriptionsListResponse()
    {
    }

    public System.Collections.Generic.List<SubscriptionDto> Subscriptions { get; set; } = new();
}
