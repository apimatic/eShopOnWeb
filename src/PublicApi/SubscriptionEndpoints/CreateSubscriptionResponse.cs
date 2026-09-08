using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    /// <summary>
    /// True when a new subscription was created; false when an equivalent live subscription
    /// already existed and was returned instead (idempotent replay).
    /// </summary>
    public bool IsNew { get; set; }

    public SubscriptionDto Subscription { get; set; } = new SubscriptionDto();
}
