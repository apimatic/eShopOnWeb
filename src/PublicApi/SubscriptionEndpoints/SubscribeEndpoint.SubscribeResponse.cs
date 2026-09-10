using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscribeResponse()
    {
    }

    /// <summary>The active subscription for the shopper on the requested plan.</summary>
    public CustomerSubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when a matching live subscription already existed and was returned unchanged
    /// (idempotent replay / double-click); false when it was created by this request.
    /// </summary>
    public bool AlreadySubscribed { get; set; }

    public string Message { get; set; } = string.Empty;
}
