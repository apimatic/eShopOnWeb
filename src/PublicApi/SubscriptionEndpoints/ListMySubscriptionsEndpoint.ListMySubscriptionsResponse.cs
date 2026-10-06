using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response for GET api/my-subscriptions.
/// </summary>
public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListMySubscriptionsResponse()
    {
    }

    public System.Collections.Generic.List<SubscriptionDto> Subscriptions { get; set; } = new();
}