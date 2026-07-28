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

    /// <summary>The subscription the shopper now holds, confirmed from Maxio.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when an equivalent active subscription already existed and was returned unchanged
    /// (idempotent, double-click-safe). False when a new subscription was created.
    /// </summary>
    public bool AlreadyExisted { get; set; }

    /// <summary>A human-readable confirmation message.</summary>
    public string Message { get; set; } = string.Empty;
}
