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

    /// <summary>True when a new subscription was created; false when an existing live subscription was returned (idempotent retry).</summary>
    public bool Created { get; set; }

    public SubscriptionDto? Subscription { get; set; }
}
