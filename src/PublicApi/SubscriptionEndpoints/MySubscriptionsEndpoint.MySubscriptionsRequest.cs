using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request for the authenticated user's subscriptions.
/// </summary>
public class MySubscriptionsRequest : BaseRequest
{
}

/// <summary>
/// Response containing the authenticated user's subscriptions in the billing
/// system of record.
/// </summary>
public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionSummaryDto> Subscriptions { get; set; } = new();
}
