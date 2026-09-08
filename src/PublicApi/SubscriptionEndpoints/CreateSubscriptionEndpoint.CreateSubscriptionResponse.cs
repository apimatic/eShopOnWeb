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

    /// <summary>
    /// The subscription, whether it was just created or already existed (idempotent replay).
    /// </summary>
    public SubscriptionSummaryDto Subscription { get; set; }

    /// <summary>
    /// True when this call created the subscription; false when an existing live subscription to the
    /// same plan was returned (a double-click/duplicate request never creates a second one).
    /// </summary>
    public bool SubscriptionCreated { get; set; }
}
