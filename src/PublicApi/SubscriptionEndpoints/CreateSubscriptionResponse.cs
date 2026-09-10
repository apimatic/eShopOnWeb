using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }

    public CreateSubscriptionResponse() { }

    public SubscriptionDto Subscription { get; set; } = new();

    /// <summary>
    /// True when the caller was already actively subscribed to this plan and the existing
    /// subscription was returned (idempotent replay) rather than a new one being created.
    /// </summary>
    public bool AlreadyExisted { get; set; }
}
