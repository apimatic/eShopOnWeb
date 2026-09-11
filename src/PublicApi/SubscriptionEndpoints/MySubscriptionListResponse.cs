using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionListResponse : BaseResponse
{
    public MySubscriptionListResponse() { }
    public MySubscriptionListResponse(System.Guid correlationId) : base(correlationId) { }
    public List<MySubscriptionDto> Subscriptions { get; set; } = new();
}
