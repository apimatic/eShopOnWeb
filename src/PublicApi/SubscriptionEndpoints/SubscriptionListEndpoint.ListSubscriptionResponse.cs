using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionResponse : BaseResponse
{
    public ListSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionResponse()
    {
    }

    public System.Collections.Generic.List<SubscriptionDto> Subscriptions { get; set; } = new();
}
