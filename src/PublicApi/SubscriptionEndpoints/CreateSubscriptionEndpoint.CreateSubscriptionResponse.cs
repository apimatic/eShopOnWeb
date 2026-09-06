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

    /// <summary>The subscription the shopper now holds.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the request did not enrol anyone because an equivalent subscription already
    /// existed - a repeated or double-clicked request. The subscription returned is the existing one.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}
