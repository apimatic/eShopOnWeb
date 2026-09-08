using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Response for <c>POST /api/subscriptions</c>.</summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    /// <summary>True when a brand-new subscription was created; false when the shopper already had the plan.</summary>
    public bool Created { get; set; }

    public SubscriptionDto? Subscription { get; set; }
}
