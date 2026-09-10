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

    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when an existing live subscription was returned instead of creating a new one
    /// (idempotent replay of a repeated / double-clicked subscribe request).
    /// </summary>
    public bool AlreadyExisted { get; set; }
}
