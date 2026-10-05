using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response listing the authenticated user's subscriptions.
/// </summary>
public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId) { }

    public ListMySubscriptionsResponse() { }

    public List<SubscriptionDetailsDto> Subscriptions { get; set; } = new();
}