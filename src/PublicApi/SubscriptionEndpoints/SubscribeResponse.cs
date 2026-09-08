using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Response for POST /api/subscriptions.</summary>
public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscribeResponse()
    {
    }

    /// <summary>The subscription now in effect for the current user and requested plan.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// <c>true</c> when a new Maxio subscription was created by this request;
    /// <c>false</c> when an existing subscription was returned (idempotent re-subscribe).
    /// </summary>
    public bool Created { get; set; }
}
