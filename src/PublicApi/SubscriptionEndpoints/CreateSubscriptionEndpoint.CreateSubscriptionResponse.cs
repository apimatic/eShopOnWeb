using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Response for a successful (or idempotently replayed) subscribe operation.</summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    /// <summary>The subscription as recorded in Maxio.</summary>
    public SubscriptionDto Subscription { get; set; } = new SubscriptionDto();

    /// <summary>True when this call created a brand new subscription; false when an existing subscription was returned.</summary>
    public bool Created { get; set; }
}
