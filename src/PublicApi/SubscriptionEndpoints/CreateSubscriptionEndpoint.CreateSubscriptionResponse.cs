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

    /// <summary>The subscription the caller now holds.</summary>
    public SubscriptionDto Subscription { get; set; } = new SubscriptionDto();

    /// <summary>
    /// True when the caller was already subscribed to this plan and nothing new was created — the
    /// answer to a retry or a double click.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}
