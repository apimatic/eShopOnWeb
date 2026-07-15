using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Shared response shape for every endpoint that returns a single subscription's current state.</summary>
public class SubscriptionResponse : BaseResponse
{
    public SubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionResponse()
    {
    }

    public SubscriptionDto Subscription { get; set; } = default!;
}
