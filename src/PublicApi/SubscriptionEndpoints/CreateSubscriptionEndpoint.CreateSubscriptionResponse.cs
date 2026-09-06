using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    /// <summary>The subscription the shopper now holds, confirmed by the billing provider.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the request resolved to a subscription that already existed - a repeat submit, a
    /// double-click or a retry - and nothing new was created. The response is returned with HTTP 200
    /// in that case, and HTTP 201 when a subscription was actually created.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}
