using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionsEndpoints;

/// <summary>Response for GET api/my-subscriptions.</summary>
public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse()
    {
    }

    public MySubscriptionsResponse(System.Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new List<SubscriptionDto>();
}
