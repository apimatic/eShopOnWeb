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

    /// <summary><c>created</c>, <c>already_subscribed</c> or <c>pending</c> (outcome not confirmed yet; check
    /// <c>GET /api/my-subscriptions</c> or retry).</summary>
    public string Outcome { get; set; } = string.Empty;

    public string Reference { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    public SubscriptionDto? Subscription { get; set; }
}
