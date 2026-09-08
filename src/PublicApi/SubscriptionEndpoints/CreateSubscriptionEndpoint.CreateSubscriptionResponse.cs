using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse()
    {
    }

    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionSummaryDto Subscription { get; set; } = new();

    /// <summary>
    /// True when the shopper was already subscribed to this plan and no new subscription
    /// was created (idempotent double-click protection).
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}
