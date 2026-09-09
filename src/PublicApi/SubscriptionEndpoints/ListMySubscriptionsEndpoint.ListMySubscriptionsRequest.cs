using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to list the authenticated user's subscriptions
/// </summary>
public class ListMySubscriptionsRequest : BaseRequest
{
    /// <summary>
    /// Populated from the JWT identity by the endpoint; not supplied by callers.
    /// </summary>
    public string Username { get; set; } = string.Empty;
}

/// <summary>
/// Response containing the user's subscriptions
/// </summary>
public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListMySubscriptionsResponse()
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
