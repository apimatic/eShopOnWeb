using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response listing the authenticated user's subscriptions.
/// </summary>
public class GetMySubscriptionsResponse : BaseResponse
{
    public GetMySubscriptionsResponse() { }

    public GetMySubscriptionsResponse(Guid correlationId) : base(correlationId) { }

    public List<SubscriptionDto> Subscriptions { get; set; } = new List<SubscriptionDto>();
}